# Hosting on IIS
All versions of IIS (including Microsoft Azure PaaS) deny serving files when
the extension MIME type is unknown. If you are hosting your updates in this
manner then you will need to add a Web.config to your downloads repository as
follows:

    <?xml version="1.0" encoding="UTF-8"?>
    <configuration>
      <system.webServer>
        <staticContent>
          <mimeMap fileExtension=".nupkg" mimeType="application/zip" />
          <mimeMap fileExtension="." mimeType="text/plain" />
        </staticContent>
      </system.webServer>
    </configuration>

*eg:* ~/downloads/Web.config
