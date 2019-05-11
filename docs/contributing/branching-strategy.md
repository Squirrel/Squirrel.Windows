| [docs](..)  / [contributing](.) / branching-strategy.md
|:---|

# tl;dr

1. Fork Squirrel.Windows on GitHub
2. Send features/fixes via pull request targeting the default branch: `develop`


# Branching Strategy

Squirrel.Windows uses a very lightweight rendition of [gitflow](https://nvie.com/posts/a-successful-git-branching-model/).

* `master` branch - the `master` branch is where official releases of squirrel are built. Changes to `master` are made only via merge commits from the `develop` branch. Tags are made for each each release.
* `develop` branch - the `develop` branch is where the next version of squirrel is under development. Changes to `develop` are made via pull request from forks and feature branches. So `develop` is the default branch on GitHub.
* fork - your development takes place in fork. When a feature/fix is ready, a pull request is sent to Squirrel.Windows targeting  the `develop` branch.

**Why gitflow?** This lightweight rendition of giflow adds minimal "overhead" in the `develop` branch. The `develop` branch allows us to experiment with new ideas and iterate on features. When "enough" work is completed for a release, complete integration testing--including multi-version upgrades--is done on the `develop` branch. When the testing is completed successfully, the `develop` branch is integrated into `master` and a release is automatically built and released.


## See Also

* [Building Squirrel](building-squirrel.md) - steps to build squirrel for the impatient.
* [VS Solution Overview](vs-solution-overview.md) - overview of the various projects in the Squirrel.Windows Visual Studio solution.

---
| Return: [Table of Contents](../readme.md) |
|----|
