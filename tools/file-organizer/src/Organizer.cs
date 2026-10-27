using XieBox.Contracts;
using XieBox.FileOperations;
using XieBox.Platform.UI;
namespace XieBox.FileOrganizer;
public static class Organizer { public static ToolPage Create(IPlatformContext context, string stateDirectory) => FileTaskPage.Create(new FileTaskModel(context, System.IO.Path.Combine(stateDirectory, "last-operation.json"), true, System.IO.Path.GetFullPath(System.IO.Path.Combine(stateDirectory, "..", "batch-renamer", "last-operation.json"))), true); }
