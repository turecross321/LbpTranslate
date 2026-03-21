namespace LbpTranslate.Cli;

public static class Prompt
{
    public static string Ask(string question, string? defaultValue = null)
    {
        string display = defaultValue != null ? $"{question} [{defaultValue}]: " : $"{question}: ";
        Console.Write(display);
        string? input = Console.ReadLine()?.Trim();
        return string.IsNullOrEmpty(input) && defaultValue != null ? defaultValue : input ?? "";
    }

    public static int Menu(string question, string[] options)
    {
        Console.WriteLine(question);
        for (int i = 0; i < options.Length; i++)
            Console.WriteLine($"  {i + 1}) {options[i]}");

        while (true)
        {
            Console.Write("Choice: ");
            if (int.TryParse(Console.ReadLine(), out int choice) && choice >= 1 && choice <= options.Length)
                return choice - 1;
            Console.WriteLine("Invalid choice, try again.");
        }
    }

    public static bool Confirm(string question, bool defaultYes = true)
    {
        string hint = defaultYes ? "[Y/n]" : "[y/N]";
        Console.Write($"{question} {hint}: ");
        string? input = Console.ReadLine()?.Trim().ToLower();
        if (string.IsNullOrEmpty(input)) return defaultYes;
        return input == "y" || input == "yes";
    }
}