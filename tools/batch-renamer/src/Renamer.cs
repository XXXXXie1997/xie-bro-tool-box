using XieBox.Contracts;
using XieBox.FileOperations;
using XieBox.Platform.UI;
namespace XieBox.BatchRenamer;
public static class Renamer { public static ToolPage Create(IPlatformContext context, string stateDirectory) => FileTaskPage.Create(new FileTaskModel(context, System.IO.Path.Combine(stateDirectory, "last-operation.json"), false), false); }
