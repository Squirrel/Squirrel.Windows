#pragma once
class MachineInstaller
{
public:
	MachineInstaller();
	~MachineInstaller();
	static int PerformMachineInstallSetup();
	static bool ShouldSilentInstall();
};

