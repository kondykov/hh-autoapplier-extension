export type LogLevel = "info" | "success" | "warn" | "error" | "step";

export interface LogSink {
  write(message: string, level: LogLevel): void;
}

export class ConsoleSink implements LogSink {
  private static colors: Record<LogLevel, string> = {
    info: "#2196F3",
    success: "#4CAF50",
    warn: "#FF9800",
    error: "#F44336",
    step: "#9C27B0",
  };

  write(message: string, level: LogLevel) {
    console.log(`%c[HH-AutoApply] ${message}`, `color:${ConsoleSink.colors[level]};font-weight:bold`);
  }
}

export class Logger {
  private sinks: LogSink[] = [];

  addSink(sink: LogSink) {
    this.sinks.push(sink);
  }

  log = (message: string, level: LogLevel = "info") => {
    for (const sink of this.sinks) sink.write(message, level);
  };
}
