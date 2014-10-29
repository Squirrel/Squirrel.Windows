#pragma once

class CFxHelper
{
public:
	static bool IsDotNet45OrHigherInstalled();
	static HRESULT InstallDotNetFramework(bool isQuiet);
};

