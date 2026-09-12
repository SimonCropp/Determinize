return await new CommandLineApplicationBuilder()
    .AddCommandsFromThisAssembly()
    .SetExecutableName("determinize")
    .SetTitle("Determinize")
    .SetDescription("Rewrites PDF and System.IO.Packaging files (nupkg, docx, xlsx, pptx) so the same source always produces byte-identical output.")
    .Build()
    .RunAsync();
