using LbpTranslate.Cli;

Console.WriteLine("=== LBP Level Translator ===");
Console.WriteLine();

string[] menuOptions =
[
    "Generate settings file",
    "Convert a level",
    "Convert plans to JSON",
    "Lookup GUID in map",
    "Exit",
];

int choice = Prompt.Menu("What would you like to do?", menuOptions);

switch (choice)
{
    case 0: await Commands.GenerateSettings();  break;
    case 1: await Commands.ConvertLevel();       break;
    case 2: await Commands.ConvertPlans();       break;
    case 3: await Commands.LookupGuid();         break;
    case 4: return;
}
