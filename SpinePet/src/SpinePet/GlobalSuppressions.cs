using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "WPF Application owns and disposes the tray icon during OnExit.",
    Scope = "type",
    Target = "~T:SpinePet.App")]
