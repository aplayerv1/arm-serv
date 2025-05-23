using System;
using System.Security.Cryptography;
using System.Text;

class GeneratePasswordHash
{
    static void Main(string[] args)
    {
        Webserver.Dispose();
        if (args.Length != 1)
        {
            Console.WriteLine("Usage: GeneratePasswordHash <password>");
            return;
        }

        string password = args[0];
        string hash = NewCrypt(password);
        Console.WriteLine("Hashed password:");
        Console.WriteLine(hash);
    }

    public static string NewCrypt(string value)
    {
        if (value == null) return null;

        using (SHA1 sha1 = SHA1.Create())
        {
            byte[] hash = sha1.ComputeHash(Encoding.UTF8.GetBytes(value));
            return "{" + BitConverter.ToString(hash).ToUpperInvariant() + "}";
        }
    }
}