| [docs](..) / [getting-started](.) / 3-distributing.md |
|:---|

# Step 3. Distributing

After packaging MyApp for distribution, the various files in the `Releases` directory are used to distribute MyApp to users. 
 
* **Setup Application** - the `Setup.exe` application is provided to new users to install the current version of MyApp (see [Installing](4-installing.md) for details). 
* **Update Files** - the `RELEASES` file, along with versioned full and delta packages, are used by the update process (see [Updating](5-updating.md) for details).  

## Local File Distribution

For simplicity, this Getting Started guide uses a local file system location for updates. The location is defined in the update location provided to the `UpdateManager` (see code in [Integrating: Basic Updating](1-integrating.md)).

This generally is not practical for updates, unless all your users have access to similar network path where the files could be easily placed.  



---
| Previous: [2. Packaging](2-packaging.md) | Next: [4. Installing](4-installing.md)|
|:---|:---|

