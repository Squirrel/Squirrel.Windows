| [docs](..)  / [using](.) / microsoft-iis.md
|:---|

# Microsoft IIS

If you use Microsoft IIS to distribute the necessary Squirrel files, you must provide a custom `Web.config` file as described below.

## Hosting on IIS

All versions of IIS (including Microsoft Azure PaaS) deny serving files when
the extension MIME type is unknown. If you are hosting your updates in this
manner then you will need to add a `Web.config` to your downloads repository as
follows:

**`~/downloads/Web.config` File**

~~~xml
<?xml version="1.0" encoding="UTF-8"?>
<configuration>
  <system.webServer>
    <staticContent>
      <mimeMap fileExtension=".nupkg" mimeType="application/zip" />
      <mimeMap fileExtension="." mimeType="text/plain" />
    </staticContent>
  </system.webServer>
</configuration>
~~~


---
| Return: [Table of Contents](../readme.md) |
|----|


