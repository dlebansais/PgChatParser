namespace PgChatParser;

using System;

/// <summary>
/// Called when a new line is parsed.
/// </summary>
/// <param name="sender">The sender.</param>
/// <param name="logTime">The time at wich the line was recorded in the log.</param>
/// <param name="logLine">The line content.</param>
public delegate void NewLineHandler(Parser sender, DateTime logTime, string logLine);
