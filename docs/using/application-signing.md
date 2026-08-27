| [docs](..)  / [using](.) / application-signing.md
|:---|


# Application Signing

Signing your installer with a valid code signing certificate is one of the most important things that you need to do for production apps. Both IE SmartScreen as well as virus scanning software will give a significant amount of "points" to apps that are signed correctly, preventing your users from getting scary dialogs.

Acquire a code-signing certificate - it's recommended to get a Windows Error Reporting-compatible certificate, see this [MSDN article](https://msdn.microsoft.com/library/windows/hardware/hh801887.aspx) for more information, then pass the -n parameter, which are the parameters you would pass to `signtool.exe sign` to sign the app.

Squirrel makes signing easy, as it signs all of your application's executables *as well* as the final generated Setup.exe.

An example invocation including both of these features would be something like:

~~~powershell
PM> Squirrel --releasify MyApp.1.0.0.nupkg -n "/a /f CodeCert.pfx /p MySecretCertPassword /fd sha256 /tr http://timestamp.digicert.com /td sha256"
~~~

If you are using the [Visual Studio Build Packaging](visual-studio-packaging.md) process be careful how you escape your quotation marks in the `XML` of your `.csproj` file for the -n, --signWithParams argument. The wrapping quotation marks must be defined in `XML` safe ampersand escape strings or `&quot;`. Within this command you will likely need quotation marks around your certificate password. Since this is already within a quoted string you will need to double quote the password: `/p &quot;&quot;PASSWORD&quot;&quot;`.

~~~xml
  <Target Name="AfterBuild" Condition=" '$(Configuration)' == 'Release'">
    <GetAssemblyIdentity AssemblyFiles="$(TargetPath)">
      <Output TaskParameter="Assemblies" ItemName="myAssemblyInfo" />
    </GetAssemblyIdentity>
    <Exec Command="nuget pack MyApp.nuspec -Version %(myAssemblyInfo.Version) -Properties Configuration=Release -OutputDirectory $(OutDir) -BasePath $(OutDir)" />
    <!-- Notice the use of &quot; rather than " after the \n flag. For the password to contain spaces we need to double-&quot; the string.  -->
    <Exec Command="squirrel --releasify $(OutDir)MyApp.$([System.Version]::Parse(%(myAssemblyInfo.Version)).ToString(3)).nupkg -n &quot;/a /f .\CertificateInProjectFolder.pfx /p &quot;&quot;CERTIFICATE PASSWORD&quot;&quot; /fd sha256 /tr http://timestamp.digicert.com /td sha256&quot;" />
  </Target>
~~~



## See Also
* [Squirrel Command Line](squirrel-command-line.md) - command line options for `Squirrel --releasify`
* [Visual Studio Build Packaging](visual-studio-packaging.md) - integrating Squirrel packaging into your build process


---
| Return: [Table of Contents](../readme.md) |
|----|
