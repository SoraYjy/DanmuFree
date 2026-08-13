using DanmuFree.Core.Protocol;

namespace DanmuFree.Tests.Protocol;

/// <summary>
/// WBI 签名回归：用 SocialSisterYi/bilibili-API-collect 文档里的固定 key/参数向量锁死算法。
/// key 变了（每日更替）算法不变，故用文档常量回归即可。
/// </summary>
public class WbiSignTests
{
    // 文档示例 key（当前全站口令，回归用）。
    private const string ImgKey = "7cd084941338484aae1ad9425b84077c";
    private const string SubKey = "4932caff0ff746eab6f01bf08b70ac45";

    [Fact]
    public void MixinKey_matches_reference_vector()
    {
        Assert.Equal("ea1db124af3c7062474693fa704f4ff8", WbiSign.GetMixinKey(ImgKey, SubKey));
    }

    [Fact]
    public void Sign_matches_reference_vector()
    {
        var mixinKey = WbiSign.GetMixinKey(ImgKey, SubKey);
        var query = WbiSign.Sign(
            new Dictionary<string, string>
            {
                ["foo"] = "114",
                ["bar"] = "514",
                ["zab"] = "1919810",
            },
            mixinKey,
            unixNow: 1702204169);

        Assert.Equal(
            "bar=514&foo=114&wts=1702204169&zab=1919810&w_rid=8f6f2b5b3d485fe1886cec6a0be8c5d4",
            query);
    }

    [Fact]
    public void Sign_for_danmu_only_uses_id_type_wts()
    {
        // getDanmuInfo 实际签名：参数 id/type，w_rid 应只依赖 id/type/wts（纯数字，编码恒等）。
        var mixinKey = WbiSign.GetMixinKey(ImgKey, SubKey);
        var query = WbiSign.Sign(
            new Dictionary<string, string> { ["id"] = "545068", ["type"] = "0" },
            mixinKey,
            unixNow: 1702204169);
        // 结构：升序 id,type,wts + w_rid；w_rid 是 32 位 hex。
        Assert.StartsWith("id=545068&type=0&wts=1702204169&w_rid=", query);
        Assert.Equal(32, query[(query.LastIndexOf('=') + 1)..].Length);
    }
}
