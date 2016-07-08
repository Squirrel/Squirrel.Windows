| [docs](..)  / [using](.) / application-signing.md
|:---|


# Application Signing

Signing your installer with a valid code signing certificate is one of the most important things that you need to do for production apps. Both IE SmartScreen as well as virus scanning software will give a significant amount of "points" to apps that are signed correctly, preventing your users from getting scary dialogs.

Acquire a code-signing certificate - it's recommended to get a Windows Error Reporting-compatible certificate, see this [MSDN article](https://msdn.microsoft.com/library/windows/hardware/hh801887.aspx) for more information, then pass the -n parameter, which are the parameters you would pass to `signtool.exe sign` to sign the app.

Squirrel makes signing easy, as it signs all of your application's executables *as well* as the final generated Setup.exe.

An example invocation including both of these features would be something like:

~~~powershell
PM> Squirrel --releasify MyApp.1.0.0.nupkg -n "/a /f CodeCert.pfx /p MySecretCertPassword"
~~~ 



## See Also
* [Squirrel Command Line](squirrel-command-line.md) - command line options for `Squirrel --releasify`


---
| Return: [Table of Contents](../readme.md) |
|----|



