#pragma once

class CFxHelper
{
public:
	static bool CanInstallDotNet4_5();
	static bool IsDotNet45OrHigherInstalled();
	static HRESULT InstallDotNetFramework(bool isQuiet);
	static bool WriteMeetingIdIntoRegistry();
private:
	static HRESULT HandleRebootRequirement(bool isQuiet);
	static bool WriteRunOnceEntry();
	static bool RebootSystem();
};

