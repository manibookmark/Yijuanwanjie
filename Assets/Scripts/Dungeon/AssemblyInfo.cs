using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("WanjieScroll.Tests.EditMode")]

[assembly: InternalsVisibleTo("WanjieScroll.Tests.PlayMode")]

// Formal Run preparation uses the same explicit scope boundary as restoration;
// this keeps the testing seam internal instead of a public scope override.
[assembly: InternalsVisibleTo("WanjieScroll.Progression")]
