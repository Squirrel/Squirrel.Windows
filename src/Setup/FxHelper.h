#pragma once

class CFxHelper
{
public:
	static bool IsDotNet45OrHigherInstalled(void);
	static void HelpUserInstallDotNetFramework(bool isQuiet);
};

