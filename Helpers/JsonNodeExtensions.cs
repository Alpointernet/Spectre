using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace Spectre;

public static class JsonNodeExtensions
{
    public static JsonNode? SelectToken(this JsonNode? node, string pathStr)
    {
        return SelectTokens(node, pathStr).FirstOrDefault();
    }

    public static IEnumerable<JsonNode> SelectTokens(this JsonNode? node, string path)
    {
        if (node == null || string.IsNullOrEmpty(path)) yield break;

        if (path.StartsWith("$.")) path = path.Substring(2);
        else if (path.StartsWith("$")) path = path.Substring(1);

        var tokens = ParsePath(path);
        foreach (var result in Evaluate(node, tokens, 0))
        {
            yield return result;
        }
    }

    private static List<string> ParsePath(string path)
    {
        var tokens = new List<string>();
        int i = 0;
        while (i < path.Length)
        {
            if (path[i] == '.')
            {
                if (i + 1 < path.Length && path[i + 1] == '.')
                {
                    tokens.Add("..");
                    i += 2;
                }
                else
                {
                    i++;
                }
            }
            else if (path[i] == '[')
            {
                int end = path.IndexOf(']', i);
                if (end > i)
                {
                    tokens.Add(path.Substring(i, end - i + 1));
                    i = end + 1;
                }
                else
                {
                    i++;
                }
            }
            else
            {
                int nextDot = path.IndexOf('.', i);
                int nextBracket = path.IndexOf('[', i);
                int end = path.Length;
                if (nextDot != -1 && nextBracket != -1) end = Math.Min(nextDot, nextBracket);
                else if (nextDot != -1) end = nextDot;
                else if (nextBracket != -1) end = nextBracket;

                tokens.Add(path.Substring(i, end - i));
                i = end;
            }
        }
        return tokens;
    }

    private static IEnumerable<JsonNode> Evaluate(JsonNode node, List<string> tokens, int tokenIndex)
    {
        if (node == null) yield break;
        if (tokenIndex >= tokens.Count)
        {
            yield return node;
            yield break;
        }

        string token = tokens[tokenIndex];

        if (token == "..")
        {
            // Recursive descent
            // First, try matching the remaining path on the current node
            foreach (var match in Evaluate(node, tokens, tokenIndex + 1))
            {
                yield return match;
            }

            // Then search children
            if (node is JsonObject obj)
            {
                foreach (var kvp in obj)
                {
                    if (kvp.Value != null)
                    {
                        foreach (var match in Evaluate(kvp.Value, tokens, tokenIndex))
                        {
                            yield return match;
                        }
                    }
                }
            }
            else if (node is JsonArray arr)
            {
                foreach (var item in arr)
                {
                    if (item != null)
                    {
                        foreach (var match in Evaluate(item, tokens, tokenIndex))
                        {
                            yield return match;
                        }
                    }
                }
            }
        }
        else if (token.StartsWith("[") && token.EndsWith("]"))
        {
            string inner = token.Substring(1, token.Length - 2);
            if (node is JsonArray arr)
            {
                if (inner == "*")
                {
                    foreach (var item in arr)
                    {
                        if (item != null)
                        {
                            foreach (var match in Evaluate(item, tokens, tokenIndex + 1))
                            {
                                yield return match;
                            }
                        }
                    }
                }
                else if (int.TryParse(inner, out int index))
                {
                    if (index >= 0 && index < arr.Count)
                    {
                        var item = arr[index];
                        if (item != null)
                        {
                            foreach (var match in Evaluate(item, tokens, tokenIndex + 1))
                            {
                                yield return match;
                            }
                        }
                    }
                }
            }
        }
        else
        {
            // Property access
            if (node is JsonObject obj && obj.TryGetPropertyValue(token, out var val) && val != null)
            {
                foreach (var match in Evaluate(val, tokens, tokenIndex + 1))
                {
                    yield return match;
                }
            }
        }
    }
}
