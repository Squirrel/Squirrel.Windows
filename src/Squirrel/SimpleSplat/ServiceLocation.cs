using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Squirrel.SimpleSplat
{
    public static class SquirrelLocator
    {
        [ThreadStatic] static IDependencyResolver unitTestDependencyResolver;
        static IDependencyResolver dependencyResolver;

        static readonly List<Action> resolverChanged = new List<Action>();

        static SquirrelLocator()
        {
            var r = new ModernDependencyResolver();
            dependencyResolver = r;

            RegisterResolverCallbackChanged(() => {
                if (SquirrelLocator.CurrentMutable == null) return;
                SquirrelLocator.CurrentMutable.InitializeSplat();
            });
        }

        /// <summary>
        /// Gets or sets the dependency resolver. This class is used throughout
        /// libraries for many internal operations as well as for general use
        /// by applications. If this isn't assigned on startup, a default, highly
        /// capable implementation will be used, and it is advised for most people
        /// to simply use the default implementation.
        /// </summary>
        /// <value>The dependency resolver.</value>
        public static IDependencyResolver Current {
            get {
                return unitTestDependencyResolver ?? dependencyResolver;
            }
            set {
                if (ModeDetector.InUnitTestRunner()) {
                    unitTestDependencyResolver = value;
                    dependencyResolver = dependencyResolver ?? value;
                } else {
                    dependencyResolver = value;
                }

                var currentCallbacks = default(Action[]);
                lock (resolverChanged) {
                    // NB: Prevent deadlocks should we reenter this setter from 
                    // the callbacks
                    currentCallbacks = resolverChanged.ToArray();
                }

                foreach (var block in currentCallbacks) block();
            }
        }

        /// <summary>
        /// Convenience property to return the DependencyResolver cast to a
        /// MutableDependencyResolver. The default resolver is also a mutable
        /// resolver, so this will be non-null. Use this to register new types
        /// on startup if you are using the default resolver
        /// </summary>
        public static IMutableDependencyResolver CurrentMutable {
            get { return Current as IMutableDependencyResolver; }
            set { Current = value; }
        }

        /// <summary>
        /// This method allows libraries to register themselves to be set up
        /// whenever the dependency resolver changes. Applications should avoid
        /// this method, it is usually used for libraries that depend on service
        /// location.
        /// </summary>
        /// <param name="callback">A callback that is invoked when the 
        /// resolver is changed. This callback is also invoked immediately,
        /// to configure the current resolver.</param>
        /// <returns>When disposed, removes the callback. You probably can 
        /// ignore this.</returns>
        public static IDisposable RegisterResolverCallbackChanged(Action callback)
        {
            lock (resolverChanged) {
                resolverChanged.Add(callback);
            }

            // NB: We always immediately invoke the callback to set up the 
            // current resolver with whatever we've got
            callback();

            return new ActionDisposable(() => {
                lock (resolverChanged) resolverChanged.Remove(callback);
            });
        }
    }

    /// <summary>
    /// Represents a dependency resolver, a service to look up global class 
    /// instances or types.
    /// </summary>
    public interface IDependencyResolver : IDisposable
    {
        /// <summary>
        /// Gets an instance of the given <paramref name="serviceType"/>. Must return <c>null</c>
        /// if the service is not available (must not throw).
        /// </summary>
        /// <param name="serviceType">The object type.</param>
        /// <returns>The requested object, if found; <c>null</c> otherwise.</returns>
        object GetService(Type serviceType, string contract = null);

        /// <summary>
        /// Gets all instances of the given <paramref name="serviceType"/>. Must return an empty
        /// collection if the service is not available (must not return <c>null</c> or throw).
        /// </summary>
        /// <param name="serviceType">The object type.</param>
        /// <returns>A sequence of instances of the requested <paramref name="serviceType"/>. The sequence
        /// should be empty (not <c>null</c>) if no objects of the given type are available.</returns>
        IEnumerable<object> GetServices(Type serviceType, string contract = null);
    }

    /// <summary>
    /// Represents a dependency resolver where types can be registered after 
    /// setup.
    /// </summary>
    public interface IMutableDependencyResolver : IDependencyResolver
    {
        void Register(Func<object> factory, Type serviceType, string contract = null);

        /// <summary>
        /// Register a callback to be called when a new service matching the type 
        /// and contract is registered.
        /// 
        /// When registered, the callback is also called for each currently matching 
        /// service.
        /// </summary>
        /// <returns>When disposed removes the callback</returns>
        /// <param name="serviceType">Service type.</param>
        /// <param name="contract">Contract.</param>
        /// <param name="callback">Callback.</param>
        IDisposable ServiceRegistrationCallback(Type serviceType, string contract, Action<IDisposable> callback);
    }

    public static class DependencyResolverMixins
    {
        /// <summary>
        /// Gets an instance of the given <paramref name="serviceType"/>. Must return <c>null</c>
        /// if the service is not available (must not throw).
        /// </summary>
        /// <param name="serviceType">The object type.</param>
        /// <returns>The requested object, if found; <c>null</c> otherwise.</returns>
        public static T GetService<T>(this IDependencyResolver This, string contract = null)
        {
            return (T)This.GetService(typeof(T), contract);
        }

        /// <summary>
        /// Gets all instances of the given <paramref name="serviceType"/>. Must return an empty
        /// collection if the service is not available (must not return <c>null</c> or throw).
        /// </summary>
        /// <param name="serviceType">The object type.</param>
        /// <returns>A sequence of instances of the requested <paramref name="serviceType"/>. The sequence
        /// should be empty (not <c>null</c>) if no objects of the given type are available.</returns>
        public static IEnumerable<T> GetServices<T>(this IDependencyResolver This, string contract = null)
        {
            return This.GetServices(typeof(T), contract).Cast<T>();
        }

        public static IDisposable ServiceRegistrationCallback(this IMutableDependencyResolver This, Type serviceType, Action<IDisposable> callback)
        {
            return This.ServiceRegistrationCallback(serviceType, null, callback);
        }

        /// <summary>
        /// Override the default Dependency Resolver until the object returned 
        /// is disposed.
        /// </summary>
        /// <param name="resolver">The test resolver to use.</param>
        public static IDisposable WithResolver(this IDependencyResolver resolver)
        {
            var origResolver = SquirrelLocator.Current;
            SquirrelLocator.Current = resolver;

            return new ActionDisposable(() => SquirrelLocator.Current = origResolver);
        }
                
        public static void RegisterConstant(this IMutableDependencyResolver This, object value, Type serviceType, string contract = null)
        {
            This.Register(() => value, serviceType, contract);
        }

        public static void RegisterLazySingleton(this IMutableDependencyResolver This, Func<object> valueFactory, Type serviceType, string contract = null)
        {
            var val = new Lazy<object>(valueFactory, LazyThreadSafetyMode.ExecutionAndPublication);
            This.Register(() => val.Value, serviceType, contract);
        }

        public static void InitializeSplat(this IMutableDependencyResolver This)
        {
            This.Register(() => new DefaultLogManager(), typeof(ILogManager));
            This.Register(() => new DebugLogger(), typeof(ILogger));
        }
    }

    /// <summary>
    /// This class is a dependency resolver written for modern C# 5.0 times. 
    /// It implements all registrations via a Factory method. With the power
    /// of Closures, you can actually implement most lifetime styles (i.e.
    /// construct per call, lazy construct, singleton) using this.
    ///
    /// Unless you have a very compelling reason not to, this is the only class
    /// you need in order to do dependency resolution, don't bother with using
    /// a full IoC container.
    /// </summary>
    public class ModernDependencyResolver : IMutableDependencyResolver
    {
        Dictionary<Tuple<Type, string>, List<Func<object>>> _registry;
        Dictionary<Tuple<Type, string>, List<Action<IDisposable>>> _callbackRegistry;

        public ModernDependencyResolver() : this(null) { }

        protected ModernDependencyResolver(Dictionary<Tuple<Type, string>, List<Func<object>>> registry)
        {
            _registry = registry != null ? 
                registry.ToDictionary(k => k.Key, v => v.Value.ToList()) :
                new Dictionary<Tuple<Type, string>, List<Func<object>>>();

            _callbackRegistry = new Dictionary<Tuple<Type, string>, List<Action<IDisposable>>>();
        }

        public void Register(Func<object> factory, Type serviceType, string contract = null)
        {
            var pair = Tuple.Create(serviceType, contract ?? string.Empty);
            if (!_registry.ContainsKey(pair)) {
                _registry[pair] = new List<Func<object>>();
            }

            _registry[pair].Add(factory);

            if (_callbackRegistry.ContainsKey(pair)) {
                List<Action<IDisposable>> toRemove = null;

                foreach (var callback in _callbackRegistry[pair]) {
                    var remove = false;
                    var disp = new ActionDisposable(() => {
                        remove = true;
                    });

                    callback(disp);

                    if (remove) {
                        if (toRemove == null) {
                            toRemove = new List<Action<IDisposable>>();
                        }

                        toRemove.Add(callback);
                    }
                }

                if (toRemove != null) {
                    foreach (var c in toRemove) {
                        _callbackRegistry[pair].Remove(c);
                    }
                }
            }
        }

        public object GetService(Type serviceType, string contract = null)
        {
            var pair = Tuple.Create(serviceType, contract ?? string.Empty);
            if (!_registry.ContainsKey(pair)) return default(object);

            var ret = _registry[pair].Last();
            return ret();
        }

        public IEnumerable<object> GetServices(Type serviceType, string contract = null)
        {
            var pair = Tuple.Create(serviceType, contract ?? string.Empty);
            if (!_registry.ContainsKey(pair)) return Enumerable.Empty<object>();

            return _registry[pair].Select(x => x()).ToList();
        }

        public IDisposable ServiceRegistrationCallback(Type serviceType, string contract, Action<IDisposable> callback)
        {
            var pair = Tuple.Create(serviceType, contract ?? string.Empty);

            if (!_callbackRegistry.ContainsKey(pair)) {
                _callbackRegistry[pair] = new List<Action<IDisposable>>();
            }

            _callbackRegistry[pair].Add(callback);

            var disp = new ActionDisposable(() => {
                _callbackRegistry[pair].Remove(callback);
            });

            if (_registry.ContainsKey(pair)) {
                foreach (var s in _registry[pair]) {
                    callback(disp);
                }
            }

            return disp;
        }

        public ModernDependencyResolver Duplicate()
        {
            return new ModernDependencyResolver(_registry);
        }

        public void Dispose()
        {
            _registry = null;
        }
    }

    /// <summary>
    /// A simple dependency resolver which takes Funcs for all its actions.
    /// GetService is always implemented via GetServices().LastOrDefault()
    /// </summary>
    public class FuncDependencyResolver : IMutableDependencyResolver
    {
        readonly Func<Type, string, IEnumerable<object>> innerGetServices;
        readonly Action<Func<object>, Type, string> innerRegister;
        readonly Dictionary<Tuple<Type, string>, List<Action<IDisposable>>> _callbackRegistry = 
            new Dictionary<Tuple<Type, string>, List<Action<IDisposable>>>();

        IDisposable inner;

        public FuncDependencyResolver(
            Func<Type, string, IEnumerable<object>> getAllServices, 
            Action<Func<object>, Type, string> register = null, 
            IDisposable toDispose = null)
        {
            innerGetServices = getAllServices;
            innerRegister = register;
            inner = toDispose ?? ActionDisposable.Empty;
        }

        public object GetService(Type serviceType, string contract = null)
        {
            return (GetServices(serviceType, contract) ?? Enumerable.Empty<object>()).LastOrDefault();
        }

        public IEnumerable<object> GetServices(Type serviceType, string contract = null)
        {
            return innerGetServices(serviceType, contract);
        }

        public void Dispose() 
        { 
            Interlocked.Exchange(ref inner, ActionDisposable.Empty).Dispose();
        }

        public void Register(Func<object> factory, Type serviceType, string contract = null)
        {
            if (innerRegister == null) throw new NotImplementedException();
            innerRegister(factory, serviceType, contract);

            var pair = Tuple.Create(serviceType, contract ?? string.Empty);

            if (_callbackRegistry.ContainsKey(pair)) {
                List<Action<IDisposable>> toRemove = null;

                foreach (var callback in _callbackRegistry[pair]) {
                    var remove = false;
                    var disp = new ActionDisposable(() => {
                        remove = true;
                    });

                    callback(disp);

                    if (remove) {
                        if (toRemove == null) {
                            toRemove = new List<Action<IDisposable>>();
                        }

                        toRemove.Add(callback);
                    }
                }

                if (toRemove != null) {
                    foreach (var c in toRemove) {
                        _callbackRegistry[pair].Remove(c);
                    }
                }
            }
        }

        public IDisposable ServiceRegistrationCallback(Type serviceType, string contract, Action<IDisposable> callback)
        {
            var pair = Tuple.Create(serviceType, contract ?? string.Empty);

            if (!_callbackRegistry.ContainsKey(pair)) {
                _callbackRegistry[pair] = new List<Action<IDisposable>>();
            }

            _callbackRegistry[pair].Add(callback);

            return new ActionDisposable(() => {
                _callbackRegistry[pair].Remove(callback);
            });
        }
    }

    sealed class ActionDisposable : IDisposable
    {
        Action block;

        public static IDisposable Empty {
            get { return new ActionDisposable(() => {}); }
        }

        public ActionDisposable(Action block)
        {
            this.block = block;
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref block, () => {})();
        }
    }
}
