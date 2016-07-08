| [docs](..)  / [using](.) / amazon-s3.md
|:---|

# Amazon S3

Amazon S3 can be used as an easy mechanism to host your releases

## Amazon S3 Setup

The following steps setup an S3 account and prepares MyApp for distribution.

1. **Register for Amazon AWS** - if you haven't already, register for an Amazon AWS account and go to the AWS Console.
2. **Create Bucket** - create a new bucket to hold your application updates
3. **Update the Package Location** - update the package location on the `UpdateManager` in MyApp to use the S3 `Link` address for the files minus the actual file name. This is the address for downloading the file and is similar to the following address:  
    `https://s3-us-west-2.amazonaws.com/myapp.bucket`
4. **Build, Pack, Releasify** - perform the necessary steps to build, package, and releasify MyApp for distribution.
3. **Upload Files** - upload the files from the Squirrel `Releases` directory into the S3 bucket.
4. **Make Public** - make the files public by selecting the files and performing the "Make Public" action.

## Amazon S3 Updates

After you have setup your S3 account, the following steps will distribute a new package for release.

4. **Build, Pack, Releasify** - perform the necessary steps to build, package, and releasify MyApp for distribution.
3. **Upload Files** - upload the new files from the Squirrel `Releases` directory. Make sure to include the new `Setup.exe` and `RELEASES` file along with any full and delta files for the new version.
4. **Make Public** - make the new files public by selecting the files and performing the "Make Public" action.


---
| Return: [Table of Contents](../readme.md) |
|----|



