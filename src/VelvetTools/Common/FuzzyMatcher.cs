namespace VelvetTools.Common;

/// <summary>启动器用的轻量模糊匹配打分。</summary>
public static class FuzzyMatcher
{
    /// <summary>返回得分，0 表示不匹配。target 应为小写。</summary>
    public static int Score(string targetLower, string queryLower)
    {
        if (queryLower.Length == 0) return 1;
        if (targetLower.Length == 0) return 0;

        if (targetLower == queryLower) return 1000;
        if (targetLower.StartsWith(queryLower)) return 800 - targetLower.Length;

        int idx = targetLower.IndexOf(queryLower, StringComparison.Ordinal);
        if (idx > 0)
        {
            // 词首命中（前一个字符是分隔符）得分更高
            char prev = targetLower[idx - 1];
            bool wordStart = prev is ' ' or '-' or '_' or '.' or '(' or '（';
            return (wordStart ? 700 : 500) - idx - targetLower.Length / 4;
        }

        // 首字母缩写匹配：例如 "vsc" -> "visual studio code"
        if (queryLower.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9'))
        {
            int qi = 0;
            bool newWord = true;
            foreach (char c in targetLower)
            {
                if (qi >= queryLower.Length) break;
                if (newWord && c == queryLower[qi]) qi++;
                newWord = c is ' ' or '-' or '_' or '.';
            }
            if (qi >= queryLower.Length) return 400 - targetLower.Length / 4;
        }

        // 顺序子序列匹配（宽松兜底，要求 query 较短）
        if (queryLower.Length >= 2 && queryLower.Length <= 6)
        {
            int qi = 0;
            foreach (char c in targetLower)
            {
                if (qi < queryLower.Length && c == queryLower[qi]) qi++;
            }
            if (qi >= queryLower.Length) return 100 - targetLower.Length / 8;
        }

        return 0;
    }
}
