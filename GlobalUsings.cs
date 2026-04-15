// Disambiguate 'Application' project-wide: WPF wins over WinForms.
// WinForms is only used for NotifyIcon (TrayService), which qualifies it fully.
global using Application = System.Windows.Application;
