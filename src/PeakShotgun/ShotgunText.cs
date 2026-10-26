using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using HarmonyLib;

namespace Peak.Shotgun;

/// <summary>
/// Player-facing text in every PEAK language. PEAK looks text up in <see cref="LocalizedText.mainTable"/>
/// (item names under <c>NAME_&lt;ITEMNAME&gt;</c>, prompts under the prompt string itself), and PEAKLib only adds
/// an English row when a key is missing. This adds our rows from the embedded <c>localization/strings.csv</c>,
/// then from <c>BepInEx/config/PeakShotgun.localization.csv</c> when a player made one, and re-adds them
/// whenever PEAK reloads its table (<see cref="LocalizedTextReloadPatch"/>).
/// </summary>
internal static class ShotgunText
{
    /// <summary><c>UIData.itemName</c>; PEAK shows row <c>NAME_SHOTGUN</c>.</summary>
    internal const string ShotgunItemName = "Shotgun";

    /// <summary><c>UIData.itemName</c>; PEAK shows row <c>NAME_AMMO PILE</c>.</summary>
    internal const string AmmoPileItemName = "Ammo Pile";

    /// <summary>The blowgun's prompt, already translated by PEAK.</summary>
    internal const string ShootPrompt = "SHOOT";

    internal const string RefillPrompt = "PEAKSHOTGUN_REFILL";

    internal const string OverrideFileName = "PeakShotgun.localization.csv";

    private const string ResourceName = "Peak.Shotgun.localization.strings.csv";

    private static Dictionary<string, string[]>? rows;

    private static bool injecting;

    private static bool reportedFailure;

    internal static string Refill => LocalizedText.GetText(RefillPrompt);

    /// <summary>Adds (or re-adds) our rows to PEAK's table, overwriting PEAKLib's English-only ones. Never throws.</summary>
    internal static void Inject()
    {
        if (injecting)
        {
            return;
        }

        injecting = true;
        try
        {
            rows ??= Load();

            // Reading mainTable can load it, which re-enters through LocalizedTextReloadPatch; the flag stops that.
            Dictionary<string, List<string>> table = LocalizedText.mainTable;
            foreach (KeyValuePair<string, string[]> row in rows)
            {
                table[row.Key] = new List<string>(row.Value);
            }
        }
        catch (Exception e)
        {
            if (!reportedFailure)
            {
                reportedFailure = true;
                Plugin.Log.LogWarning($"Could not add shotgun translations; names may show in English or as LOC keys: {e.Message}");
            }
        }
        finally
        {
            injecting = false;
        }
    }

    private static Dictionary<string, string[]> Load()
    {
        int languages = Enum.GetValues(typeof(LocalizedText.Language)).Length;
        var loaded = new Dictionary<string, string[]>();
        using (Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName))
        {
            if (stream == null)
            {
                Plugin.Log.LogWarning($"Translation resource {ResourceName} was not found.");
            }
            else
            {
                using var reader = new StreamReader(stream, Encoding.UTF8);
                Merge(loaded, reader.ReadToEnd(), languages, ResourceName);
            }
        }

        string overridePath = Path.Combine(Paths.ConfigPath, OverrideFileName);
        if (File.Exists(overridePath))
        {
            Merge(loaded, File.ReadAllText(overridePath, Encoding.UTF8), languages, overridePath);
            Plugin.Log.LogInfo($"Loaded translation overrides from {overridePath}.");
        }

        foreach (KeyValuePair<string, string[]> row in loaded)
        {
            string english = string.IsNullOrEmpty(row.Value[0]) ? row.Key : row.Value[0];
            for (int i = 0; i < row.Value.Length; i++)
            {
                // PEAK falls back to English for an empty cell but throws on a null one.
                if (string.IsNullOrEmpty(row.Value[i]))
                {
                    row.Value[i] = english;
                }
            }
        }

        return loaded;
    }

    /// <summary>
    /// CSV with a header row: <c>Key</c>, then one column per <see cref="LocalizedText.Language"/> name in any order.
    /// Non-empty cells replace what an earlier file set. Rows whose key starts with <c>#</c> are comments.
    /// </summary>
    private static void Merge(Dictionary<string, string[]> into, string csv, int languages, string source)
    {
        List<List<string>> records = ParseCsv(csv);
        if (records.Count == 0)
        {
            return;
        }

        List<string> header = records[0];
        int[] columnLanguage = new int[header.Count];
        for (int column = 0; column < header.Count; column++)
        {
            columnLanguage[column] = -1;
            if (column > 0 && Enum.TryParse(header[column].Trim(), true, out LocalizedText.Language language))
            {
                columnLanguage[column] = (int)language;
            }
            else if (column > 0)
            {
                Plugin.Log.LogWarning($"{source}: column '{header[column]}' is not a PEAK language and is ignored.");
            }
        }

        for (int r = 1; r < records.Count; r++)
        {
            List<string> record = records[r];
            string key = record.Count > 0 ? record[0].Trim().ToUpperInvariant() : "";
            if (key.Length == 0 || key[0] == '#')
            {
                continue;
            }

            if (!into.TryGetValue(key, out string[] values))
            {
                values = new string[languages];
                into[key] = values;
            }

            for (int column = 1; column < record.Count && column < columnLanguage.Length; column++)
            {
                int language = columnLanguage[column];
                if (language >= 0 && language < languages && record[column].Length > 0)
                {
                    values[language] = record[column];
                }
            }
        }
    }

    /// <summary>RFC 4180: commas, quoted fields, doubled quotes inside quotes, LF or CRLF line ends.</summary>
    private static List<List<string>> ParseCsv(string text)
    {
        var records = new List<List<string>>();
        var record = new List<string>();
        var field = new StringBuilder();
        bool quoted = false;
        int start = text.Length > 0 && text[0] == '﻿' ? 1 : 0;
        for (int i = start; i < text.Length; i++)
        {
            char c = text[i];
            if (quoted)
            {
                if (c != '"')
                {
                    field.Append(c);
                }
                else if (i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else
                {
                    quoted = false;
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    quoted = true;
                    break;
                case ',':
                    record.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    break;
                case '\n':
                    record.Add(field.ToString());
                    field.Clear();
                    records.Add(record);
                    record = new List<string>();
                    break;
                default:
                    field.Append(c);
                    break;
            }
        }

        if (field.Length > 0 || record.Count > 0)
        {
            record.Add(field.ToString());
            records.Add(record);
        }

        return records;
    }
}

/// <summary>PEAK rebuilds its text table from scratch here, which drops every mod row; add ours back.</summary>
[HarmonyPatch(typeof(LocalizedText), nameof(LocalizedText.LoadMainTable))]
internal static class LocalizedTextReloadPatch
{
    [HarmonyPostfix]
    private static void Postfix() => ShotgunText.Inject();
}
