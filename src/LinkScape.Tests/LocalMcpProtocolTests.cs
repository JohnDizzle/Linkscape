using System.Text;
using System.Text.Json.Nodes;

[TestClass]
public sealed class LocalMcpProtocolTests
{
    [TestMethod]
    public async Task WriteMessageAsync_WritesNewlineDelimitedJson()
    {
        var message = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = 1,
            ["result"] = new JsonObject()
        };
        using var output = new MemoryStream();

        await LocalMcpProtocol.WriteMessageAsync(output, message);

        var written = Encoding.UTF8.GetString(output.ToArray());
        Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{}}\n", written);
        Assert.IsFalse(written.Contains("Content-Length", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ReadMessageAsync_ReadsConsecutiveUtf8JsonLines()
    {
        const string inputText =
            "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\"}\n" +
            "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/call\",\"params\":{\"label\":\"Café\"}}\n";
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(inputText));

        var first = await LocalMcpProtocol.ReadMessageAsync(input);
        var second = await LocalMcpProtocol.ReadMessageAsync(input);

        Assert.AreEqual("initialize", first?["method"]?.GetValue<string>());
        Assert.AreEqual("tools/call", second?["method"]?.GetValue<string>());
        Assert.AreEqual("Café", second?["params"]?["label"]?.GetValue<string>());
    }
}
