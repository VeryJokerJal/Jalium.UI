namespace Jalium.UI;

/// <summary>Represents the method that handles application startup.</summary>
public delegate void StartupEventHandler(object sender, StartupEventArgs e);

/// <summary>Represents the method that handles application shutdown.</summary>
public delegate void ExitEventHandler(object sender, ExitEventArgs e);

/// <summary>Represents the method that handles an operating-system session-ending request.</summary>
public delegate void SessionEndingCancelEventHandler(object sender, SessionEndingCancelEventArgs e);
