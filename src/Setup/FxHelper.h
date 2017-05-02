#pragma once

enum class NetVersion {net45=0, net451=1, net452=2, net46=3, net461=4, net462=5};

class CFxHelper
{
public:
	static NetVersion GetRequiredDotNetVersion();
	static bool CanInstallDotNet4_5();
	static bool IsDotNetInstalled(NetVersion requiredVersion);
	static HRESULT InstallDotNetFramework(NetVersion version, bool isQuiet);
private:
	static HRESULT HandleRebootRequirement(bool isQuiet);
	static bool WriteRunOnceEntry();
	static bool RebootSystem();
	static int GetDotNetVersionReleaseNumber(NetVersion version);
	static UINT GetInstallerUrlForVersion(NetVersion version);
	static UINT GetInstallerMainInstructionForVersion(NetVersion version);
	static UINT GetInstallerContentForVersion(NetVersion version);
	static UINT GetInstallerExpandedInfoForVersion(NetVersion version);
};

