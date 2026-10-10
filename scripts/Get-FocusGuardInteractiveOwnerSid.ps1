[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
if ($null -eq ('FocusGuardInteractiveSessionIdentity' -as [type])) {
    Add-Type -TypeDefinition @"
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
public static class FocusGuardInteractiveSessionIdentity {
 [DllImport("wtsapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern bool WTSQuerySessionInformation(IntPtr server,int session,int info,out IntPtr buffer,out int bytes);
 [DllImport("wtsapi32.dll")] private static extern void WTSFreeMemory(IntPtr buffer);
 private static string Read(int session,int kind) { IntPtr data;int size;if(!WTSQuerySessionInformation(IntPtr.Zero,session,kind,out data,out size))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());try{return Marshal.PtrToStringUni(data)??"";}finally{WTSFreeMemory(data);} }
 public static string GetSid() { using(var process=Process.GetCurrentProcess()){if(process.SessionId==0)throw new InvalidOperationException("Explicit owner required outside interactive session.");var user=Read(process.SessionId,5);var domain=Read(process.SessionId,7);if(string.IsNullOrWhiteSpace(user))throw new InvalidOperationException("Interactive owner unavailable.");var account=new NTAccount(string.IsNullOrEmpty(domain)?user:domain+"\\"+user);return ((SecurityIdentifier)account.Translate(typeof(SecurityIdentifier))).Value;} }
}
"@
}
[FocusGuardInteractiveSessionIdentity]::GetSid()
