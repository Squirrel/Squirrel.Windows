| [docs](..)  / [using](.) / staged-rollouts.md
|:---|

# Staged Rollouts

Staged rollouts allow you to distribute the latest version of your app to a subset of users that you can increase over time, similar to rollouts on platforms like Google Play. This feature requires Squirrel.Windows 1.4.0 or above.

### How to use

Staged rollouts are controlled by manually editing your `RELEASES` file. Here's an example:

~~~
e3f67244e4166a65310c816221a12685c83f8e6f myapp-1.0.0-full.nupkg 600725
~~~

Now let's ship a new version to 10% of our userbase.

```
e3f67244e4166a65310c816221a12685c83f8e6f myapp-1.0.0-full.nupkg 600725
0d777ea94c612e8bf1ea7379164caefba6e24463 myapp-1.0.1-delta.nupkg 6030# 10%
85f4d657f8424dd437d1b33cc4511ea7ad86b1a7 myapp-1.0.1-full.nupkg 600752# 10%
```

Note that the syntax is `# nn%` - due to a bug in earlier versions of Squirrel.Windows, for now, you *must* put the `#` immediately following the file size, no spaces. Once all of your users have Squirrel 1.4.0 or higher, you can add a space after the `#` (similar to a comment).

Assuming that this rollout is going well, at some point you can upload a new version of the releases file:

```
e3f67244e4166a65310c816221a12685c83f8e6f myapp-1.0.0-full.nupkg 600725
0d777ea94c612e8bf1ea7379164caefba6e24463 myapp-1.0.1-delta.nupkg 6030# 50%
85f4d657f8424dd437d1b33cc4511ea7ad86b1a7 myapp-1.0.1-full.nupkg 600752# 50%
```

When you're confident that this release has gone successfully, you can remove the comment so that 100% of users get the file:

```
e3f67244e4166a65310c816221a12685c83f8e6f myapp-1.0.0-full.nupkg 600725
0d777ea94c612e8bf1ea7379164caefba6e24463 myapp-1.0.1-delta.nupkg 6030
85f4d657f8424dd437d1b33cc4511ea7ad86b1a7 myapp-1.0.1-full.nupkg 600752
```

### Handling failed rollouts

If you want to pull a staged release because it hasn't gone well, you should hand-edit the RELEASES file to completely remove the bad version:

~~~
e3f67244e4166a65310c816221a12685c83f8e6f myapp-1.0.0-full.nupkg 600725
~~~

Once you do this, you **must** increment the version number higher than your broken release (in this example, we would need to release MyApp 1.0.2). Because some of your users will be on the broken 1.0.1, releasing a _new_ 1.0.1 would result in them staying on a broken version.
