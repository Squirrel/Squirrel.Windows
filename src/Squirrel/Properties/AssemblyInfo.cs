using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

[assembly: ComVisible(false)]
[assembly: InternalsVisibleTo("Squirrel.Tests, PublicKey=" + SNK.SHA1)]
[assembly: InternalsVisibleTo("Squirrel, PublicKey=" + SNK.SHA1)]
[assembly: InternalsVisibleTo("Squirrel.CommandLine, PublicKey=" + SNK.SHA1)]
[assembly: InternalsVisibleTo("Squirrel.CommandLine.Windows, PublicKey=" + SNK.SHA1)]
[assembly: InternalsVisibleTo("Update, PublicKey=" + SNK.SHA1)]
[assembly: InternalsVisibleTo("Update.Windows, PublicKey=" + SNK.SHA1)]

internal static class SNK
{
    public const string SHA1 = "002400000480000094000000060200000024000052534131000400000100010061b199572531d267773d7783a077bc020aacb34a10d8c11407505a4a814284d4c953df3229ccf8f63d1a410a3395b7266e5e5cba8f1c0bc9ee10fc7ddafdae297431e2eef82eccd2ac8957bfc9119063f4a965d6ae3ccf53e1f4d8e9ce894a79ea1f681eb2067745c5253f6747cbc51eec640dd2edb4a67339b44f093e3ec5b0";
}
