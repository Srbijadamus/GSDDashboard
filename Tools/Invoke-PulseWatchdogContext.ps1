param(
    [Parameter(Mandatory)][int]$WatchdogProcessId,
    [Parameter(Mandatory)][string]$Executable,
    [Parameter(Mandatory)][string]$WorkingDirectory,
    [Parameter(Mandatory)][string]$RuntimeConfig,
    [Parameter(Mandatory)][string]$Receipt,
    [switch]$StartIsolated,
    [int]$Port = 5058
)
$ErrorActionPreference = 'Stop'
foreach ($path in @($Executable, $WorkingDirectory, $RuntimeConfig, $Receipt)) {
    if (![IO.Path]::IsPathRooted($path)) { throw 'Absolute paths required.' }
}
$configAcl = Get-Acl -LiteralPath $RuntimeConfig
if (!$configAcl.AreAccessRulesProtected) { throw 'Runtime file must have a protected SYSTEM/Administrators DACL.' }
foreach ($rule in $configAcl.Access) {
    $sid = $rule.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value
    if ($rule.AccessControlType -eq 'Allow' -and $sid -notin @('S-1-5-18','S-1-5-32-544')) {
        throw 'Runtime file grants access beyond SYSTEM/Administrators.'
    }
}
$watchdog = Get-CimInstance Win32_Process -Filter "ProcessId=$WatchdogProcessId"
if (!$watchdog -or $watchdog.ExecutablePath -ine "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" -or
    $watchdog.CommandLine -notmatch 'watchdog_gsd_backend\.ps1') { throw 'Expected production watchdog required.' }
if ($StartIsolated) {
    if ($Port -notin @(5057,5058) -or (Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue)) {
        throw 'Only an unused isolated test port is allowed.'
    }
    $settings = Get-Content -LiteralPath (Join-Path $WorkingDirectory 'appsettings.json') -Raw | ConvertFrom-Json
    $db = New-Object System.Data.SqlClient.SqlConnectionStringBuilder($settings.ConnectionStrings.DefaultConnection)
    if ($db.InitialCatalog -notin @('GSDDashboardTest','GSDDashboardCopyTest')) { throw 'Isolated test database required.' }
}
if (!(Test-Path -LiteralPath $Executable -PathType Leaf)) { throw 'Apphost missing.' }
. (Join-Path $PSScriptRoot 'LiveDataTargets.ps1')
if (!(Test-Path -LiteralPath (Split-Path $Receipt))) { throw 'Protected receipt directory must exist.' }
Set-DataProtectionAcl (Split-Path $Receipt)
if (!(Test-Path -LiteralPath $Receipt)) { New-Item -ItemType File -Path $Receipt | Out-Null }
Set-DataProtectionAcl $Receipt
Clear-Content -LiteralPath $Receipt
Add-Type @'
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.ComponentModel;
public static class PulseWatchdogContext {
    [DllImport("kernel32.dll",SetLastError=true)] static extern IntPtr OpenProcess(uint access,bool inherit,int pid);
    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    [DllImport("advapi32.dll",SetLastError=true)] static extern bool OpenProcessToken(IntPtr process,uint access,out IntPtr token);
    [DllImport("advapi32.dll",SetLastError=true,CharSet=CharSet.Unicode)] static extern bool LookupPrivilegeValue(string system,string name,out long luid);
    [StructLayout(LayoutKind.Sequential,Pack=4)] struct Privilege { public int Count; public long Luid; public int Attributes; }
    [DllImport("advapi32.dll",SetLastError=true)] static extern bool AdjustTokenPrivileges(IntPtr token,bool disable,ref Privilege state,int length,IntPtr previous,IntPtr returned);
    [DllImport("advapi32.dll",SetLastError=true)] static extern bool DuplicateTokenEx(IntPtr token,uint access,IntPtr attributes,int level,int type,out IntPtr duplicate);
    [DllImport("advapi32.dll",SetLastError=true)] static extern bool GetTokenInformation(IntPtr token,int kind,out int value,int length,out int returned);
    [DllImport("advapi32.dll",SetLastError=true)] static extern bool ImpersonateLoggedOnUser(IntPtr token);
    [DllImport("advapi32.dll",SetLastError=true)] static extern bool RevertToSelf();
    [DllImport("advapi32.dll")] static extern bool IsTokenRestricted(IntPtr token);
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct Startup {
        public int Size; public string Reserved,Desktop,Title;
        public int X,Y,XSize,YSize,XChars,YChars,Fill,Flags;
        public short Show,Reserved2; public IntPtr ReservedPtr,Input,Output,Error;
    }
    [StructLayout(LayoutKind.Sequential)] struct ProcessInfo { public IntPtr Process,Thread; public int Pid,Tid; }
    [DllImport("advapi32.dll",SetLastError=true,CharSet=CharSet.Unicode)]
    static extern bool CreateProcessWithTokenW(IntPtr token,int flags,string application,System.Text.StringBuilder command,int creation,IntPtr environment,string directory,ref Startup startup,out ProcessInfo process);
    [DllImport("kernel32.dll",SetLastError=true)] static extern uint WaitForSingleObject(IntPtr handle,uint milliseconds);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool GetExitCodeProcess(IntPtr process,out int code);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool TerminateProcess(IntPtr process,int code);
    static void Enable(string name) {
        IntPtr token; if(!OpenProcessToken(GetCurrentProcess(),0x28,out token)) throw new Win32Exception();
        try { long luid; if(!LookupPrivilegeValue(null,name,out luid)) throw new Win32Exception();
            var state=new Privilege { Count=1,Luid=luid,Attributes=2 };
            if(!AdjustTokenPrivileges(token,false,ref state,0,IntPtr.Zero,IntPtr.Zero)) throw new Win32Exception();
            int error=Marshal.GetLastWin32Error(); if(error!=0) throw new Win32Exception(error);
        } finally { CloseHandle(token); }
    }
    public static string Run(int pid,string executable,string arguments,string directory,string path,bool wait) {
        Enable("SeDebugPrivilege"); Enable("SeImpersonatePrivilege");
        IntPtr process=OpenProcess(0x1000,false,pid),token=IntPtr.Zero,duplicate=IntPtr.Zero;
        if(process==IntPtr.Zero) throw new Win32Exception();
        try {
            if(!OpenProcessToken(process,0xB,out token)) throw new Win32Exception();
            if(!DuplicateTokenEx(token,0x02000000,IntPtr.Zero,2,1,out duplicate)) throw new Win32Exception();
            int elevated,returned; if(!GetTokenInformation(duplicate,20,out elevated,4,out returned)) throw new Win32Exception();
            string identity; bool admin;
            using(var user=new WindowsIdentity(duplicate)) {
                identity=user.User.Value;
                if(!ImpersonateLoggedOnUser(duplicate)) throw new Win32Exception();
                try {
                    admin=new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
                    Console.WriteLine("Watchdog token: elevated="+(elevated!=0)+", AdministratorsEnabled="+admin+", Restricted="+IsTokenRestricted(duplicate)+", AdministratorsSidPresent="+(user.Groups!=null && user.Groups.Contains(new SecurityIdentifier("S-1-5-32-544"))));
                    try {
                        using(var file=File.OpenRead(path)) { if(file.Length==0) throw new InvalidOperationException("Runtime file empty."); }
                    } catch(UnauthorizedAccessException) {
                        return "{\"WatchdogPid\":"+pid+",\"Elevated\":"+(elevated!=0?"true":"false")+",\"AdministratorEnabled\":"+(admin?"true":"false")+",\"AdministratorSidPresent\":"+(user.Groups!=null && user.Groups.Contains(new SecurityIdentifier("S-1-5-32-544"))?"true":"false")+",\"FileOpen\":\"DENIED\",\"ExitCode\":-1}";
                    }
                    if(!admin && identity!="S-1-5-18") throw new InvalidOperationException("Watchdog must run as SYSTEM or with enabled Administrators membership.");
                } finally { if(!RevertToSelf()) throw new Win32Exception(); }
            }
            var startup=new Startup { Size=Marshal.SizeOf(typeof(Startup)) };
            ProcessInfo child;
            var command=new System.Text.StringBuilder("\""+executable+"\" "+arguments);
            if(!CreateProcessWithTokenW(duplicate,0,executable,command,0,IntPtr.Zero,directory,ref startup,out child)) throw new Win32Exception();
            try {
                int exit=259;
                if(wait) {
                    if(WaitForSingleObject(child.Process,60000)!=0) {
                        TerminateProcess(child.Process,1);
                        throw new TimeoutException("Watchdog-token preflight did not finish within 60 seconds.");
                    }
                    if(!GetExitCodeProcess(child.Process,out exit)) throw new Win32Exception();
                }
                return "{\"WatchdogPid\":"+pid+",\"ChildPid\":"+child.Pid+",\"TokenUserSid\":\""+identity+"\",\"Elevated\":"+(elevated!=0?"true":"false")+",\"AdministratorEnabled\":"+(admin?"true":"false")+",\"FileOpen\":\"PASSED\",\"ExitCode\":"+exit+"}";
            } finally { CloseHandle(child.Thread); CloseHandle(child.Process); }
        } finally {
            if(duplicate!=IntPtr.Zero)CloseHandle(duplicate); if(token!=IntPtr.Zero)CloseHandle(token); CloseHandle(process);
        }
    }
}
'@
$arguments = '--Pulse:RuntimeConfigPath="' + $RuntimeConfig + '"'
if ($StartIsolated) { $arguments += ' --urls=http://127.0.0.1:'+$Port }
else { $arguments += ' --Pulse:ConfigPreflight=true' }
$previous = $env:PULSE_RUNTIME_CONFIG
try {
    $env:PULSE_RUNTIME_CONFIG = $null
    $result = [PulseWatchdogContext]::Run($WatchdogProcessId,$Executable,$arguments,$WorkingDirectory,$RuntimeConfig,!$StartIsolated)
    $result | Set-Content -LiteralPath $Receipt -Encoding UTF8
    $result
    $evidence = $result | ConvertFrom-Json
    if ($evidence.FileOpen -ne 'PASSED' -or (!$StartIsolated -and $evidence.ExitCode -ne 0)) { throw 'Watchdog-context preflight failed.' }
} catch {
    if (!(Get-Item -LiteralPath $Receipt).Length) {
        @{Result='FAILED';WatchdogPid=$WatchdogProcessId;ExceptionType=$_.Exception.GetType().FullName} |
            ConvertTo-Json | Set-Content -LiteralPath $Receipt -Encoding UTF8
    }
    throw
} finally {
    $env:PULSE_RUNTIME_CONFIG = $previous
    Set-DataProtectionAcl $Receipt
}
