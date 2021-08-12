#pragma once

enum class NetVersion {net45=0, net451=1, net452=2, net46=3, net461=4, net462=5, net47=6, net471=7, net472=8, net48=9};

class CFxHelper
{
public:
	static bool IsNet50Installed();
private:
	// CS - these are public methods i've relocated, 
	// since I need net5.0 and can't be bothered to implement support for it properly
	static NetVersion GetRequiredDotNetVersion();
	static bool CanInstallDotNet4_5();
	static bool IsDotNetInstalled(NetVersion requiredVersion);
	static HRESULT InstallDotNetFramework(NetVersion version, bool isQuiet);
	// these are real private methods
	static HRESULT HandleRebootRequirement(bool isQuiet);
	static bool WriteRunOnceEntry();
	static bool RebootSystem();
	static UINT GetDotNetVersionReleaseNumber(NetVersion version);
	static UINT GetInstallerUrlForVersion(NetVersion version);
	static UINT GetInstallerMainInstructionForVersion(NetVersion version);
	static UINT GetInstallerContentForVersion(NetVersion version);
	static UINT GetInstallerExpandedInfoForVersion(NetVersion version);
};
