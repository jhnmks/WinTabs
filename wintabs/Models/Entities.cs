using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reactive.Threading.Tasks;
using System.Runtime.Intrinsics.Arm;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace wintabs.Models
{
    public interface IObservableFileEntity<T> : IObservable<T>
    {
        [JsonIgnore]
        string FullPath { get; }

        IObservable<T> Value { get; }

        Task SaveAsync(Func<T, T> update);
    }

    public abstract class ObservableFileEntityBase<T> : IObservableFileEntity<T>
    {
        [JsonIgnore]
        public string FullPath { get; }

        public IObservable<T> Value => m_value;

        private readonly IObservable<T> m_value;
        private readonly Subject<T> m_saves = new();
        private T m_currentValue = default!;
        private readonly SemaphoreSlim m_rwLock = new(1, 1);
        private Task m_initTask;

        protected ObservableFileEntityBase(string fullPath, Func<T> defaultFactory)
        {
            FullPath = fullPath;

            async Task<T> readAsync()
            {
                if (File.Exists(FullPath))
                {
                    try { return await ReadFileAsync(FullPath); }
                    catch (Exception e) when (IsFileOrFormatException(e))
                    {
                        if (File.Exists(FullPath))
                            File.Move(FullPath, $"{FullPath}.{DateTime.UtcNow:yyyyMMdd-HHmmssfffffff}.corrupt");
                    }
                }

                T value = defaultFactory();
                var backup = FullPath + ".bak";
                if (File.Exists(backup))
                {
                    try { value = await ReadFileAsync(backup); }
                    catch (Exception e) when (IsFileOrFormatException(e)) { }
                }
                await WriteFileAsync(value);
                return value;
            }

            async Task<T> ReadFileAsync(string path)
            {
                using var stream = File.OpenRead(path);
                return await ReadAsync(stream);
            }

            var lastValue = Observable.FromAsync(readAsync)
                .Merge(m_saves)
                .DistinctUntilChanged()
                .Do(value => m_currentValue = value)
                .Replay(1);
            lastValue.Connect();

            m_value = lastValue;
            m_initTask = lastValue.FirstOrDefaultAsync().ToTask();
        }

        private static bool IsFileOrFormatException(Exception? e) => e is FileNotFoundException || e is JsonException || e is FormatException || IsFileOrFormatException(e?.InnerException);

        public IDisposable Subscribe(IObserver<T> observer)
        {
            return Value.Subscribe(observer);
        }

        public virtual async Task SaveAsync(Func<T, T> update)
        {
            await m_initTask;
            await m_rwLock.WaitAsync();
            try
            {
                var newValue = update(m_currentValue);
                if (Equals(m_currentValue, newValue))
                    return;

                await WriteFileAsync(newValue);
                m_saves.OnNext(newValue);
            }
            finally
            {
                m_rwLock.Release();
            }
        }

        protected abstract Task<T> ReadAsync(Stream stream);
        protected abstract Task WriteAsync(Stream stream, T value);

        private async Task WriteFileAsync(T value)
        {
            var tempPath = FullPath + ".tmp";
            try
            {
                using (var stream = File.Create(tempPath))
                {
                    await WriteAsync(stream, value);
                    stream.Flush(flushToDisk: true);
                }

                if (File.Exists(FullPath))
                    File.Replace(tempPath, FullPath, FullPath + ".bak");
                else
                    File.Move(tempPath, FullPath);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new InvalidOperationException($"Permission denied writing to {FullPath}.", ex);
            }
            catch (IOException ex)
            {
                throw new InvalidOperationException($"I/O error writing to {FullPath}.", ex);
            }
            finally
            {
                try { File.Delete(tempPath); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
        }
    }

    public class ObservableJsonEntity<T>(string fullPath, Func<T> defaultFactory, JsonSerializerOptions? options = null) : ObservableFileEntityBase<T>(fullPath, defaultFactory)
    {
        public JsonSerializerOptions Options { get; } = options ?? new JsonSerializerOptions();

        protected override async Task<T> ReadAsync(Stream stream)
        {
            var result = await JsonSerializer.DeserializeAsync<T>(stream, Options) ?? throw new JsonException("Deserialized value is null.");
            return result;
        }

        protected override async Task WriteAsync(Stream stream, T value)
        {
            await JsonSerializer.SerializeAsync(stream, value, Options);
            await stream.FlushAsync();
        }
    }

    public class ObservableJsonEntityWithCommentPreservation<T>(string fullPath, Func<T> defaultFactory, JsonSerializerOptions? options = null) : ObservableFileEntityBase<T>(fullPath, defaultFactory)
    {
        public JsonSerializerOptions Options { get; } = options ?? new JsonSerializerOptions();

        private readonly JsonSerializerOptions m_readOptions = new(options ?? new JsonSerializerOptions())
        {
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        protected override async Task<T> ReadAsync(Stream stream)
        {
            var result = await JsonSerializer.DeserializeAsync<T>(stream, m_readOptions) ?? throw new JsonException("Deserialized value is null.");
            return result;
        }

        protected override async Task WriteAsync(Stream stream, T value)
        {
            using var newValueStream = new MemoryStream();
            await JsonSerializer.SerializeAsync(newValueStream, value, Options);
            newValueStream.Position = 0;

            try
            {
                using var oldValueStream = File.OpenRead(FullPath);
                await MergeJsonPreservingComments(stream, newValueStream, oldValueStream);
            }
            catch (Exception)
            {
                stream.SetLength(0);
                stream.Position = 0;
                newValueStream.Position = 0;
                await newValueStream.CopyToAsync(stream);
            }

            await stream.FlushAsync();
        }

        private static async Task<byte[]> ReadAllAsync(Stream stream)
        {
            byte[] b = new byte[stream.Length - stream.Position];
            await stream.ReadExactlyAsync(b);
            return b;
        }

        private async Task MergeJsonPreservingComments(Stream outputStream, Stream newValueStream, Stream oldValueStream)
        {
            var newValueBytes = await ReadAllAsync(newValueStream);
            var oldValueBytes = await ReadAllAsync(oldValueStream);

            var readerOptions = new JsonReaderOptions { CommentHandling = JsonCommentHandling.Allow, AllowTrailingCommas = true };
            var writerOptions = new JsonWriterOptions { Indented = Options.WriteIndented, Encoder = Options.Encoder };

            using var writer = new Utf8JsonWriter(outputStream, writerOptions);
            void Merge()
            {
                var newReader = new Utf8JsonReader(newValueBytes, readerOptions);
                var oldReader = new Utf8JsonReader(oldValueBytes, readerOptions);
                newReader.Read();
                oldReader.Read();
                MergeWithComments(writer, ref newReader, ref oldReader);
            }
            Merge();
        }

        private void MergeWithComments(Utf8JsonWriter writer, ref Utf8JsonReader newReader, ref Utf8JsonReader oldReader)
        {
            CopyCommentsAndReadMore(writer, ref oldReader);
            CopyCommentsAndReadMore(writer, ref newReader);

            switch (newReader.TokenType)
            {
                case JsonTokenType.Comment:
                    throw new InvalidProgramException("Unexpected Comment");
                case JsonTokenType.None:
                    throw new InvalidProgramException("Unexpected None");
                case JsonTokenType.StartObject:
                    if (oldReader.TokenType == JsonTokenType.StartObject)
                    {
                        MergeObjectWithComments(writer, ref newReader, ref oldReader);
                    }
                    else
                    {
                        oldReader.Skip();
                        CopyValue(writer, ref newReader);
                    }
                    break;
                case JsonTokenType.EndObject:
                    throw new InvalidProgramException("Expected StartObject to eat EndObject");
                case JsonTokenType.StartArray:
                    oldReader.Skip();
                    CopyValue(writer, ref newReader);
                    Debug.Assert(newReader.TokenType == JsonTokenType.EndArray);
                    break;
                case JsonTokenType.EndArray:
                    throw new InvalidProgramException("Expected StartArray to eat EndArray");
                case JsonTokenType.PropertyName:
                    throw new InvalidProgramException("Expected StartObject to eat PropertyName");
                case JsonTokenType.String:
                case JsonTokenType.Number:
                case JsonTokenType.True:
                case JsonTokenType.False:
                case JsonTokenType.Null:
                    oldReader.Skip();
                    CopyToken(writer, ref newReader);
                    break;
            }
        }

        private void CopyToken(Utf8JsonWriter writer, ref Utf8JsonReader newReader)
        {
            switch (newReader.TokenType)
            {
                case JsonTokenType.None:
                    break;
                case JsonTokenType.StartObject:
                    writer.WriteStartObject();
                    break;
                case JsonTokenType.EndObject:
                    writer.WriteEndObject();
                    break;
                case JsonTokenType.StartArray:
                    writer.WriteStartArray();
                    break;
                case JsonTokenType.EndArray:
                    writer.WriteEndArray();
                    break;
                case JsonTokenType.PropertyName:
                    writer.WritePropertyName(newReader.ValueSpan);
                    break;
                case JsonTokenType.Comment:
                    writer.WriteCommentValue(newReader.ValueSpan);
                    break;
                case JsonTokenType.String:
                    writer.WriteStringValue(newReader.ValueSpan);
                    break;
                case JsonTokenType.Number:
                case JsonTokenType.True:
                case JsonTokenType.False:
                case JsonTokenType.Null:
                    writer.WriteRawValue(newReader.ValueSpan);
                    break;
            }
        }

        private void CopyGuts(Utf8JsonWriter writer, ref Utf8JsonReader reader)
        {
            int depth = reader.CurrentDepth;
            while (reader.Read() && (reader.TokenType == JsonTokenType.Comment || reader.CurrentDepth > depth))
            {
                CopyToken(writer, ref reader);
            }
        }

        private void CopyValue(Utf8JsonWriter writer, ref Utf8JsonReader reader)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.StartArray:
                case JsonTokenType.StartObject:
                    CopyToken(writer, ref reader);
                    CopyGuts(writer, ref reader);
                    CopyToken(writer, ref reader);
                    break;
                default:
                    CopyToken(writer, ref reader);
                    break;
            }
        }

        private void CopyCommentsAndReadMore(Utf8JsonWriter writer, ref Utf8JsonReader reader)
        {
            while (reader.TokenType == JsonTokenType.Comment)
            {
                writer.WriteCommentValue(reader.ValueSpan);
                reader.Read();
            }
        }

        private HashSet<string> GetProperties(Utf8JsonReader reader)
        {
            HashSet<string> properties = new();
            int depth = reader.CurrentDepth + 1;
            while (reader.Read())
            {
                if (depth == reader.CurrentDepth && reader.TokenType == JsonTokenType.PropertyName)
                {
                    properties.Add(reader.GetString()!);
                }
            }
            return properties;
        }

        private void Advance(ref Utf8JsonReader reader, string propertyName)
        {
            int depth = reader.CurrentDepth + 1;
            while (reader.Read())
            {
                if (depth == reader.CurrentDepth && reader.TokenType == JsonTokenType.PropertyName && reader.GetString() == propertyName)
                {
                    return;
                }
            }
        }

        private void MergeObjectWithComments(Utf8JsonWriter writer, ref Utf8JsonReader newReader, ref Utf8JsonReader oldReader)
        {
            Debug.Assert(newReader.TokenType == JsonTokenType.StartObject);
            Debug.Assert(oldReader.TokenType == JsonTokenType.StartObject);

            var newReaderKeys = GetProperties(newReader);
            var oldReaderKeys = new HashSet<string>();

            writer.WriteStartObject();

            while (oldReader.Read())
            {
                CopyCommentsAndReadMore(writer, ref oldReader);

                if (oldReader.TokenType == JsonTokenType.EndObject)
                {
                    break;
                }

                if (oldReader.TokenType != JsonTokenType.PropertyName)
                {
                    throw new InvalidProgramException("Expected PropertyName");
                }

                string propertyName = oldReader.GetString()!;
                oldReaderKeys.Add(propertyName);
                writer.WritePropertyName(oldReader.ValueSpan);

                if (newReaderKeys.Contains(propertyName))
                {
                    var newReaderCopy = newReader;
                    Advance(ref newReaderCopy, propertyName);
                    newReaderCopy.Read();
                    oldReader.Read();
                    MergeWithComments(writer, ref newReaderCopy, ref oldReader);
                    if (oldReader.TokenType == JsonTokenType.EndObject)
                    {
                        break;
                    }
                    continue;
                }

                oldReader.Read();
                CopyValue(writer, ref oldReader);
            }

            var keysToCopy = new HashSet<string>(newReaderKeys.Where(x => !oldReaderKeys.Contains(x)));
            if (keysToCopy.Count > 0)
            {
                int depth = newReader.CurrentDepth;
                while (newReader.Read())
                {
                    if (depth < newReader.CurrentDepth && newReader.TokenType != JsonTokenType.PropertyName)
                    {
                        continue;
                    }

                    if (newReader.TokenType == JsonTokenType.EndObject)
                    {
                        break;
                    }

                    string propertyName = newReader.GetString()!;
                    if (keysToCopy.Contains(propertyName))
                    {
                        writer.WritePropertyName(propertyName);
                        newReader.Read();
                        CopyValue(writer, ref newReader);
                    }
                }
            }

            writer.WriteEndObject();
        }
    }
}
