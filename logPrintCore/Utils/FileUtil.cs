using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace logPrintCore.Utils;

public static class FileUtil {
	private const int RM_REBOOT_REASON_NONE = 0;
	private const int CCH_RM_MAX_APP_NAME = 255;
	private const int CCH_RM_MAX_SVC_NAME = 63;


	[DllImport(dllName: "rstrtmgr.dll", CharSet = CharSet.Unicode)] private static extern int RmStartSession(out uint pSessionHandle, int dwSessionFlags, string strSessionKey);

	[DllImport(dllName: "rstrtmgr.dll")] private static extern int RmEndSession(uint pSessionHandle);

	[DllImport(dllName: "rstrtmgr.dll", CharSet = CharSet.Unicode)]
	private static extern int RmRegisterResources(
		uint pSessionHandle,
		uint nFiles,
		string[] rgsFilenames,
		uint nApplications,
		[In] RmUniqueProcess[]? rgApplications,
		uint nServices,
		string[]? rgsServiceNames
	);

	[DllImport(dllName: "rstrtmgr.dll")]
	private static extern int RmGetList(
		uint dwSessionHandle,
		out uint pnProcInfoNeeded,
		ref uint pnProcInfo,
		[In] [Out] RmProcessInfo[]? rgAffectedApps,
		ref uint lpdwRebootReasons
	);


	public static List<Process> WhoIsLocking(params string[] paths) {
		var key = Guid.NewGuid().ToString();
		List<Process> processes = [];

		var res = RmStartSession(out var handle, dwSessionFlags: 0, key);
		if (res != 0) {
			throw new Exception(message: "Could not begin restart session. Unable to determine file locker.");
		}


		try {
			const int ERROR_MORE_DATA = 234;
			uint pnProcInfo = 0;
			uint lpdwRebootReasons = RM_REBOOT_REASON_NONE;

			res = RmRegisterResources(handle, (uint)paths.Length, paths, nApplications: 0, rgApplications: null, nServices: 0, rgsServiceNames: null);
			if (res != 0) {
				throw new Exception(message: "Could not register resource.");
			}


			//NOTE: there's a race condition here -- the first call to RmGetList() returns the total number of process.
			// However, when we call RmGetList() again to get the actual processes this number may have increased.
			res = RmGetList(handle, out var pnProcInfoNeeded, ref pnProcInfo, rgAffectedApps: null, ref lpdwRebootReasons);

			if (res == ERROR_MORE_DATA) {
				// Create an array to store the process results:
				var processInfo = new RmProcessInfo[pnProcInfoNeeded];
				pnProcInfo = pnProcInfoNeeded;

				// Get the list:
				res = RmGetList(handle, out pnProcInfoNeeded, ref pnProcInfo, processInfo, ref lpdwRebootReasons);
				if (res == 0) {
					processes = new List<Process>((int)pnProcInfo);

					// Enumerate all the results and add them to the list to be returned:
					for (var i = 0; i < pnProcInfo; i++) {
						try {
							processes.Add(Process.GetProcessById(processInfo[i].Process.dwProcessId));
						} catch (ArgumentException) {
							// catch the error in case the process is no longer running.
						}
					}
				} else {
					throw new Exception(message: "Could not list processes locking resource.");
				}
			} else if (res != 0) {
				throw new Exception(message: "Could not list processes locking resource.  Failed to get size of result.");
			}
		} finally {
			RmEndSession(handle);
		}

		return processes;
	}


#pragma warning disable IDE1006	// WinAPI doesn't really give us a choice about naming conventions here.
	[StructLayout(LayoutKind.Sequential)]
	private struct RmUniqueProcess {
		public int dwProcessId;
		public FILETIME ProcessStartTime;
	}


	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct RmProcessInfo {
		public RmUniqueProcess Process;
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCH_RM_MAX_APP_NAME + 1)] public string strAppName;
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCH_RM_MAX_SVC_NAME + 1)] public string strServiceShortName;
		public RmAppType ApplicationType;
		public uint AppStatus;
		public uint TSSessionId;
		[MarshalAs(UnmanagedType.Bool)] public bool bRestartable;
	}


	private enum RmAppType {
		// ReSharper disable UnusedMember.Local
		RmUnknownApp = 0,
		RmMainWindow = 1,
		RmOtherWindow = 2,
		RmService = 3,
		RmExplorer = 4,
		RmConsole = 5,
		RmCritical = 1000
		// ReSharper restore UnusedMember.Local
	}
#pragma warning restore IDE1006
}
