using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using VelvetTools.Common;

namespace VelvetTools.Modules.Knowledge;

/// <summary>知识库里的一份源文档。</summary>
public sealed class KnowledgeDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string FileName { get; set; } = "";
    public string SourcePath { get; set; } = "";
    /// <summary>解析器实际提取到的字符数（可能因文件软上限而少于整份原文）。</summary>
    public int CharCount { get; set; }
    /// <summary>真正参与分块与向量化的字符数。</summary>
    public int IndexedCharCount { get; set; }
    public bool WasTruncated { get; set; }
    public int ChunkCount { get; set; }
    public DateTime Added { get; set; } = DateTime.Now;
}

/// <summary>一个文本块及其向量。向量单独存二进制文件，JSON 里只存元数据。</summary>
public sealed class KnowledgeChunk
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string DocumentId { get; set; } = "";
    public string DocumentName { get; set; } = "";
    /// <summary>在原文里的序号，用于展示"第几段"。</summary>
    public int Index { get; set; }
    public string Text { get; set; } = "";

    /// <summary>嵌入向量。已归一化，检索时点积即余弦相似度。</summary>
    [JsonIgnore] public float[] Vector { get; set; } = Array.Empty<float>();
}

public sealed class KnowledgeBase
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "新建知识库";
    public DateTime Created { get; set; } = DateTime.Now;

    /// <summary>建库时用的嵌入模型；换模型必须重建，否则向量空间对不上。</summary>
    public string EmbedProviderId { get; set; } = "";
    public string EmbedModel { get; set; } = "";
    public int Dimension { get; set; }
    /// <summary>
    /// 当前元数据指向的向量文件版本。版本化后可先完整写好新向量，再原子切换元数据，
    /// 避免删除/重建期间崩溃导致文本块与向量错位。
    /// </summary>
    public string VectorRevision { get; set; } = "";

    public List<KnowledgeDocument> Documents { get; set; } = new();
    public List<KnowledgeChunk> Chunks { get; set; } = new();

    [JsonIgnore] public int TotalChars => Documents.Sum(d => d.IndexedCharCount > 0 ? d.IndexedCharCount : d.CharCount);
    [JsonIgnore] public int MissingVectorCount =>
        Dimension <= 0 ? Chunks.Count : Chunks.Count(c => c.Vector.Length != Dimension);
}

/// <summary>
/// 知识库存储：元数据存 JSON，向量另存紧凑二进制。
/// 分开存是因为几千个 1536 维向量转成 JSON 数字文本会膨胀十几倍且加载极慢。
/// </summary>
public sealed class KnowledgeStore
{
    private readonly string _dir;
    private string MetaPath => Path.Combine(_dir, "bases.json");
    private string VectorPath(string baseId, string revision = "") => Path.Combine(
        _dir,
        string.IsNullOrWhiteSpace(revision)
            ? $"vectors_{baseId}.bin"
            : $"vectors_{baseId}_{revision}.bin");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public List<KnowledgeBase> Bases { get; private set; } = new();

    public KnowledgeStore(string? dataRoot = null)
    {
        _dir = Path.Combine(dataRoot ?? Logger.DataDir, "knowledge");
        Load();
    }

    private void Load()
    {
        try
        {
            Directory.CreateDirectory(_dir);
            if (File.Exists(MetaPath))
                Bases = JsonSerializer.Deserialize<List<KnowledgeBase>>(File.ReadAllText(MetaPath), JsonOpts) ?? new();

            foreach (var kb in Bases)
            {
                LoadVectors(kb);
                MigrateDocumentCounts(kb);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("读取知识库失败", ex);
            Bases = new();
        }
    }

    private void LoadVectors(KnowledgeBase kb)
    {
        string path = VectorPath(kb.Id, kb.VectorRevision);
        if (!File.Exists(path) || kb.Dimension <= 0) return;

        try
        {
            using var fs = File.OpenRead(path);
            using var reader = new BinaryReader(fs);

            // 文件可能因为上次写到一半被打断而偏短：能读几块读几块，
            // 剩下的留空（检索时跳过），不要因此把整个库判死
            long available = fs.Length / (sizeof(float) * (long)kb.Dimension);
            if (available < kb.Chunks.Count)
                Logger.Info($"知识库「{kb.Name}」向量文件只覆盖 {available}/{kb.Chunks.Count} 块，请重建索引");

            for (int c = 0; c < kb.Chunks.Count && c < available; c++)
            {
                var vec = new float[kb.Dimension];
                bool usable = false;
                bool finite = true;
                for (int i = 0; i < kb.Dimension; i++)
                {
                    vec[i] = reader.ReadSingle();
                    finite &= float.IsFinite(vec[i]);
                    usable |= Math.Abs(vec[i]) > 1e-12f;
                }
                // v0.8 会为缺失向量写全零占位。迁移时把它恢复成“缺失”，
                // 管理页即可明确提示重建，而不是悄悄拿死向量参与检索。
                kb.Chunks[c].Vector = finite && usable ? vec : Array.Empty<float>();
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"读取知识库向量失败（{kb.Name}），需要重建", ex);
            foreach (var chunk in kb.Chunks) chunk.Vector = Array.Empty<float>();
        }
    }

    /// <summary>只保存元数据。适合新建、重命名等没有改动向量顺序的操作。</summary>
    public bool Save() => SaveMetadataAtomic();

    /// <summary>
    /// 保存一个向量发生变化的知识库。新向量写入带版本号的新文件，
    /// 全部完成后才原子替换元数据；任一阶段失败，磁盘上的旧版本仍然可用。
    /// </summary>
    public bool Save(KnowledgeBase changedBase)
    {
        string oldRevision = changedBase.VectorRevision;
        string newRevision = changedBase.Dimension > 0 && changedBase.Chunks.Count > 0
            ? Guid.NewGuid().ToString("N")
            : "";
        string? newVectorPath = newRevision.Length > 0
            ? VectorPath(changedBase.Id, newRevision)
            : null;

        try
        {
            Directory.CreateDirectory(_dir);
            if (newVectorPath is not null)
            {
                WriteVectorAtomic(newVectorPath, changedBase);
            }

            changedBase.VectorRevision = newRevision;
            if (!SaveMetadataAtomic())
                throw new IOException("写入知识库元数据失败");
        }
        catch (Exception ex)
        {
            changedBase.VectorRevision = oldRevision;
            TryDelete(newVectorPath);
            Logger.Error($"保存知识库失败（{changedBase.Name}）", ex);
            return false;
        }

        // 元数据已经切到新版本，旧文件此时才可以安全删除。清理失败只会留下孤儿文件，
        // 不影响数据正确性，下次变更还会继续清理。
        CleanupVectorFiles(changedBase.Id, newVectorPath);
        return true;
    }

    public KnowledgeBase Create(string name)
    {
        var kb = new KnowledgeBase { Name = name };
        Bases.Add(kb);
        if (!Save())
        {
            Bases.Remove(kb);
            throw new IOException("无法保存新建的知识库");
        }
        return kb;
    }

    public bool Delete(KnowledgeBase kb)
    {
        int index = Bases.IndexOf(kb);
        if (index < 0) return false;
        Bases.Remove(kb);
        if (!Save())
        {
            Bases.Insert(index, kb);
            return false;
        }

        CleanupVectorFiles(kb.Id, keepPath: null);
        return true;
    }

    public bool RemoveDocument(KnowledgeBase kb, KnowledgeDocument doc)
    {
        var oldDocuments = kb.Documents.ToList();
        var oldChunks = kb.Chunks.ToList();
        string oldProvider = kb.EmbedProviderId;
        string oldModel = kb.EmbedModel;
        int oldDimension = kb.Dimension;

        kb.Documents.Remove(doc);
        kb.Chunks.RemoveAll(c => c.DocumentId == doc.Id);
        // 删空了就把模型绑定一起解开，否则库里没内容却还被旧维度卡着换不了模型
        if (kb.Chunks.Count == 0) ResetEmbedding(kb);
        if (Save(kb)) return true;

        kb.Documents = oldDocuments;
        kb.Chunks = oldChunks;
        kb.EmbedProviderId = oldProvider;
        kb.EmbedModel = oldModel;
        kb.Dimension = oldDimension;
        return false;
    }

    /// <summary>清空一个库的内容，同时解除嵌入模型绑定。</summary>
    public bool Clear(KnowledgeBase kb)
    {
        var oldDocuments = kb.Documents.ToList();
        var oldChunks = kb.Chunks.ToList();
        string oldProvider = kb.EmbedProviderId;
        string oldModel = kb.EmbedModel;
        int oldDimension = kb.Dimension;

        kb.Documents.Clear();
        kb.Chunks.Clear();
        ResetEmbedding(kb);
        if (Save(kb)) return true;

        kb.Documents = oldDocuments;
        kb.Chunks = oldChunks;
        kb.EmbedProviderId = oldProvider;
        kb.EmbedModel = oldModel;
        kb.Dimension = oldDimension;
        return false;
    }

    private bool SaveMetadataAtomic()
    {
        string? tempPath = null;
        try
        {
            Directory.CreateDirectory(_dir);
            tempPath = Path.Combine(_dir, $"bases_{Guid.NewGuid():N}.tmp");
            File.WriteAllText(tempPath, JsonSerializer.Serialize(Bases, JsonOpts));
            File.Move(tempPath, MetaPath, true);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("保存知识库元数据失败", ex);
            return false;
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    private static void WriteVectorAtomic(string destination, KnowledgeBase kb)
    {
        string tempPath = destination + ".tmp";
        try
        {
            using (var fs = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(fs))
            {
                foreach (var chunk in kb.Chunks)
                {
                    if (chunk.Vector.Length != kb.Dimension)
                        throw new InvalidDataException(
                            $"片段 {chunk.Id} 缺少 {kb.Dimension} 维向量，不能保存不完整索引");
                    if (chunk.Vector.Any(v => !float.IsFinite(v))
                        || chunk.Vector.All(v => Math.Abs(v) <= 1e-12f))
                        throw new InvalidDataException($"片段 {chunk.Id} 含无效向量数值");

                    foreach (float v in chunk.Vector) writer.Write(v);
                }
                fs.Flush(flushToDisk: true);
            }
            File.Move(tempPath, destination);
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    private void CleanupVectorFiles(string baseId, string? keepPath)
    {
        try
        {
            if (!Directory.Exists(_dir)) return;
            string prefix = $"vectors_{baseId}";
            foreach (string path in Directory.EnumerateFiles(_dir, "vectors_*.bin"))
            {
                string name = Path.GetFileNameWithoutExtension(path);
                if (!name.Equals(prefix, StringComparison.Ordinal)
                    && !name.StartsWith(prefix + "_", StringComparison.Ordinal))
                    continue;
                if (keepPath is not null
                    && Path.GetFullPath(path).Equals(Path.GetFullPath(keepPath), StringComparison.OrdinalIgnoreCase))
                    continue;
                TryDelete(path);
            }
        }
        catch (Exception ex)
        {
            Logger.Info($"清理知识库旧向量文件失败：{ex.Message}");
        }
    }

    private static void TryDelete(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch { }
    }

    private static void MigrateDocumentCounts(KnowledgeBase kb)
    {
        // v0.8 只记录了解析器报告的原字符数。遇到被 6 万字上限截断的旧文档，
        // 根据已保存分块估算真正索引的字符数，避免界面继续声称“全文已入库”。
        foreach (var doc in kb.Documents.Where(d => d.IndexedCharCount <= 0))
        {
            var chunks = kb.Chunks
                .Where(c => c.DocumentId == doc.Id)
                .OrderBy(c => c.Index)
                .ToList();

            int estimate = 0;
            for (int i = 0; i < chunks.Count; i++)
                estimate += i == 0 ? chunks[i].Text.Length : Math.Max(0, chunks[i].Text.Length - 120);

            doc.IndexedCharCount = Math.Min(doc.CharCount, estimate);
            doc.WasTruncated = doc.CharCount > doc.IndexedCharCount;
        }
    }

    /// <summary>
    /// 冒烟测试使用的隔离存储回归：验证版本化向量写入、重新加载、元数据单独保存，
    /// 以及删除最后一份文档后清理模型绑定。全程只使用系统临时目录。
    /// </summary>
    internal static void RunStorageSelfTest()
    {
        string root = Path.Combine(
            Path.GetTempPath(), $"VelvetTools-KnowledgeSelfTest-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var store = new KnowledgeStore(root);
            var kb = store.Create("self-test");
            var doc = new KnowledgeDocument
            {
                FileName = "sample.txt",
                SourcePath = Path.Combine(root, "sample.txt"),
                CharCount = 12,
                IndexedCharCount = 12,
                ChunkCount = 2,
            };
            kb.EmbedProviderId = "self-test-provider";
            kb.EmbedModel = "self-test-embedding";
            kb.Dimension = 3;
            kb.Documents.Add(doc);
            kb.Chunks.AddRange(new[]
            {
                new KnowledgeChunk
                {
                    DocumentId = doc.Id,
                    DocumentName = doc.FileName,
                    Index = 0,
                    Text = "alpha",
                    Vector = new[] { 1f, 0f, 0f },
                },
                new KnowledgeChunk
                {
                    DocumentId = doc.Id,
                    DocumentName = doc.FileName,
                    Index = 1,
                    Text = "beta",
                    Vector = new[] { 0f, 1f, 0f },
                },
            });

            if (!store.Save(kb) || string.IsNullOrWhiteSpace(kb.VectorRevision))
                throw new InvalidDataException("版本化向量未保存");

            kb.Name = "self-test-renamed";
            if (!store.Save())
                throw new InvalidDataException("元数据单独保存失败");

            var loaded = new KnowledgeStore(root);
            var loadedBase = loaded.Bases.Single();
            if (loadedBase.Name != "self-test-renamed"
                || loadedBase.Chunks.Count != 2
                || loadedBase.Chunks.Any(c => c.Vector.Length != 3)
                || Math.Abs(loadedBase.Chunks[1].Vector[1] - 1f) > 1e-6f)
                throw new InvalidDataException("知识库重新加载结果不一致");

            if (!loaded.RemoveDocument(loadedBase, loadedBase.Documents.Single()))
                throw new InvalidDataException("删除文档保存失败");

            var empty = new KnowledgeStore(root).Bases.Single();
            if (empty.Documents.Count != 0 || empty.Chunks.Count != 0
                || empty.Dimension != 0 || empty.VectorRevision.Length != 0)
                throw new InvalidDataException("空库没有正确解除模型绑定");
        }
        finally
        {
            try
            {
                if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            }
            catch { }
        }
    }

    private static void ResetEmbedding(KnowledgeBase kb)
    {
        kb.Dimension = 0;
        kb.EmbedModel = "";
        kb.EmbedProviderId = "";
    }
}
