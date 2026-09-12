// The converted bytes and, already flattened to display strings, what the library reported it
// changed to produce them. The two libraries report in different shapes, so the flattening happens
// at the call that knows which one answered rather than everywhere the detail is printed.
record DeterminizeResult(Format Format, byte[] Data, IReadOnlyList<string> Changes);
