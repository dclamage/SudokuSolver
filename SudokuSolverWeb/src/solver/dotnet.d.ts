export interface DotnetModule {
  dotnet: DotnetHostBuilder;
}

export interface DotnetHostBuilder {
  withDiagnosticTracing(enabled: boolean): DotnetHostBuilder;
  create(): Promise<DotnetRuntime>;
}

export interface DotnetRuntime {
  setModuleImports(
    moduleName: string,
    imports: Record<string, (...args: never[]) => unknown>,
  ): void;
  getAssemblyExports<TExports>(assemblyName: string): Promise<TExports>;
  getConfig(): { mainAssemblyName: string };
}

export interface SudokuSolverAssemblyExports {
  SudokuSolverWasm: {
    SolverInterop: {
      Initialize(singleThreaded: boolean): void;
      HandleMessage(messageJson: string): void;
    };
  };
}
