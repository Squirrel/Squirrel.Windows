| [Squirrel.Windows](../README.md) |
|:---|

# 3 Contributing

Why not give back and help make Squirrel even better? Here is an overview of ways you can become more involved.

* **Join the Squirrel Slack Room** - email [paul@paulbetts.org](mailto:paul@paulbetts.org) with the email address you'd like to receive an invite.
* **Contribute Documentation** - improve the documentation or provide additional code examples to benefit others.
* **Subscribe to Issues on GitHub** - have some experience using Squirrel? Help answer questions under issues or post a Pull Request fixing a bug.
* **Contribute Code** -  have a great feature that you feel is a good fit for Squirrel? Send a Pull Request.


## Additional Helps for Contributing

Here is some additional help on getting started contributing to the project.

### Visual Studio Solution Overview

Review the [high-level overview](3-Contributing-VS-Solution-Overview.md) of the Squirrel Visual Studio solution and it's projects.

**Tip:** You can compile the Squirrel.Windows solution with Visual Studio version 2013 and above (including community edition).

### Building Squirrel

For the Impatient:

```sh
git clone https://github.com/squirrel/squirrel.windows
git submodule update --init --recursive       ## THIS IS THE PART YOU PROBABLY FORGOT
.\.NuGet\NuGet.exe restore
msbuild /p:Configuration=Release
```

**Tip:** Squirrel.Windows is a fairly typical C# / C++ project, the only special part is making sure to clone submodules via the command above.


---
|Next: [4. FAQ](4-FAQ.md)|
|:---|

