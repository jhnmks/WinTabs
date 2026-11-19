/// Windows 10 1809 to 21H1
using System;
using System.Runtime.InteropServices;

namespace WinMan.Windows.Com.Build17661
{
	[ComImport]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	[Guid("FF72FFDD-BE7E-43FC-9C03-AD81681E88E4")]
	internal interface IComVirtualDesktop
	{
		bool IsViewVisible(IComApplicationView view);
		Guid GetId();
	}

	[ComImport]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	[Guid("F31574D6-B682-4CDC-BD56-1827860ABEC6")]
	internal interface IComVirtualDesktopManagerInternal
	{
		int GetCount();
		void MoveViewToDesktop(IComApplicationView view, IComVirtualDesktop desktop);
		bool CanViewMoveDesktops(IComApplicationView view);
		IComVirtualDesktop GetCurrentDesktop();
		void GetDesktops(out IComObjectArray desktops);
		[PreserveSig]
		int GetAdjacentDesktop(IComVirtualDesktop from, int direction, out IComVirtualDesktop desktop);
		void SwitchDesktop(IComVirtualDesktop desktop);
		IComVirtualDesktop CreateDesktop();
		void RemoveDesktop(IComVirtualDesktop desktop, IComVirtualDesktop fallback);
		IComVirtualDesktop FindDesktop(ref Guid desktopid);
	}
}
