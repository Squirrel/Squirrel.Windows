#pragma once

class CFxHelper
{
public:
	static bool IsDotNet45OrHigherInstalled();
	static void HelpUserInstallDotNetFramework(bool isQuiet);
};

