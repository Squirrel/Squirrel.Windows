#pragma once
#include "RuntimeInfo.h"

class CFxHelper
{

public:
    static HRESULT InstallDotnet(const RUNTIMEINFO* runtime, bool isQuiet);
    static HRESULT HandleRebootRequirement(bool isQuiet);
private:
    static bool WriteRunOnceEntry();
    static bool RebootSystem();

};