// A stand-in for WeirServer.exe in the tray's install-process tests: it does nothing but stay alive until it is killed.
// It is a program of our own on the Windows subsystem, not a system console program such as ping: a console program can
// be handed a console window when its parent is killed, and where Windows Terminal is the default terminal that opens
// a window on the desktop (#806, #821).

// Bounds how long a process orphaned by a killed test run can linger.
await Task.Delay(TimeSpan.FromMinutes(2));
