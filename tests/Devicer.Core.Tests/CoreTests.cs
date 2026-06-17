using Devicer.Core.Models;
using Devicer.Core.Services;

namespace Devicer.Core.Tests;

public class BashQuoteTests
{
    [Fact]
    public void Empty_string_returns_empty_quotes()
        => Assert.Equal("''", Bash.Quote(""));

    [Fact]
    public void Simple_string_is_single_quoted()
        => Assert.Equal("'/dev/block/sda1'", Bash.Quote("/dev/block/sda1"));

    [Fact]
    public void String_with_single_quote_is_escaped()
        => Assert.Equal("'it'\\''s'", Bash.Quote("it's"));

    [Fact]
    public void String_with_spaces_is_quoted()
        => Assert.Equal("'hello world'", Bash.Quote("hello world"));

    [Fact]
    public void String_with_special_chars_is_safe()
        => Assert.Equal("'$(rm -rf /)'", Bash.Quote("$(rm -rf /)"));

    [Fact]
    public void Multiple_single_quotes_all_escaped()
        => Assert.Equal("'a'\\''b'\\''c'", Bash.Quote("a'b'c"));
}

public class FirmwareVersionTests
{
    [Fact]
    public void Parse_4_segment()
    {
        var fv = FirmwareVersion.TryParse("S938BXXS6BYIF/S938BOXM6BYIF/S938BXXS6BYIF/S938BXXS6BYIF");
        Assert.NotNull(fv);
        Assert.Equal("S938BXXS6BYIF", fv.Pda);
        Assert.Equal("S938BOXM6BYIF", fv.Csc);
        Assert.Equal("S938BXXS6BYIF", fv.Cp);
        Assert.Equal("S938BXXS6BYIF", fv.Boot);
    }

    [Fact]
    public void Parse_3_segment()
    {
        var fv = FirmwareVersion.TryParse("PDA1/CSC1/CP1");
        Assert.NotNull(fv);
        Assert.Equal("PDA1", fv.Pda);
        Assert.Null(fv.Boot);
    }

    [Fact]
    public void Parse_rejects_2_segments()
        => Assert.Null(FirmwareVersion.TryParse("PDA/CSC"));

    [Fact]
    public void Parse_rejects_null()
        => Assert.Null(FirmwareVersion.TryParse(null));

    [Fact]
    public void Parse_rejects_empty()
        => Assert.Null(FirmwareVersion.TryParse(""));

    [Fact]
    public void ComparePda_newer_is_positive()
        => Assert.True(FirmwareVersion.ComparePda("S938BXXS9BZCH", "S938BXXS6BYIF") > 0);

    [Fact]
    public void ComparePda_older_is_negative()
        => Assert.True(FirmwareVersion.ComparePda("S938BXXS6BYIF", "S938BXXS9BZCH") < 0);

    [Fact]
    public void ComparePda_equal_is_zero()
        => Assert.Equal(0, FirmwareVersion.ComparePda("S938BXXS6BYIF", "S938BXXS6BYIF"));

    [Fact]
    public void ComparePda_null_returns_zero()
        => Assert.Equal(0, FirmwareVersion.ComparePda(null, "S938BXXS6BYIF"));

    [Fact]
    public void Normalized_fills_boot_from_pda_when_3_segments()
    {
        var fv = new FirmwareVersion("PDA", "CSC", "CP");
        Assert.Equal("PDA/CSC/CP/PDA", fv.Normalized);
    }

    [Fact]
    public void Raw_3_segment_has_no_boot()
    {
        var fv = new FirmwareVersion("PDA", "CSC", "CP");
        Assert.Equal("PDA/CSC/CP", fv.Raw);
    }
}

public class PartitionInfoTests
{
    [Theory]
    [InlineData("efs", true)]
    [InlineData("modemst1", true)]
    [InlineData("persist", true)]
    [InlineData("fsg", true)]
    [InlineData("drm", true)]
    [InlineData("boot", false)]
    [InlineData("system", false)]
    [InlineData("vendor", false)]
    [InlineData("userdata", false)]
    public void CriticalNames_classification(string name, bool expected)
        => Assert.Equal(expected, PartitionInfo.CriticalNames.Contains(name));

    [Fact]
    public void ReasonFor_efs_returns_explanation()
    {
        var reason = PartitionInfo.ReasonFor("efs");
        Assert.NotNull(reason);
        Assert.Contains("IMEI", reason);
    }

    [Fact]
    public void ReasonFor_unknown_returns_null()
        => Assert.Null(PartitionInfo.ReasonFor("system"));
}

public class OdinTarEntryTests
{
    [Theory]
    [InlineData("boot.img.lz4", "boot")]
    [InlineData("system.img.lz4", "system")]
    [InlineData("cache.img", "cache")]
    [InlineData("vbmeta.img", "vbmeta")]
    [InlineData("AP_boot.img.lz4", "ap_boot")]
    [InlineData("modem.bin", "modem")]
    [InlineData("meta-data/metadata.txt", "metadata.txt")]
    public void PartitionGuess_strips_extensions(string name, string expected)
    {
        var entry = new OdinTarEntry { Name = name, SizeBytes = 1024 };
        Assert.Equal(expected, entry.PartitionGuess);
    }

    [Theory]
    [InlineData("boot.img.lz4", true)]
    [InlineData("system.img", true)]
    [InlineData("modem.bin", true)]
    [InlineData("cache.img.lz4", true)]
    [InlineData("metadata.txt", false)]
    [InlineData("README", false)]
    public void IsImage_detects_image_extensions(string name, bool expected)
    {
        var entry = new OdinTarEntry { Name = name, SizeBytes = 100 };
        Assert.Equal(expected, entry.IsImage);
    }
}

public class OdinTarInfoTests
{
    [Theory]
    [InlineData("AP_S938BXXS6BYIF.tar.md5", "AP")]
    [InlineData("BL_S938BXXS6BYIF.tar.md5", "BL")]
    [InlineData("CP_S938BXXS6BYIF.tar.md5", "CP")]
    [InlineData("CSC_OXM_S938BOXM6BYIF.tar.md5", "CSC")]
    [InlineData("HOME_CSC_OXM_S938BOXM6BYIF.tar.md5", "HOME_CSC")]
    [InlineData("unknown.tar.md5", null)]
    public void PackageHint_from_filename(string fileName, string? expected)
    {
        var info = new OdinTarInfo
        {
            Path = $"C:\\fw\\{fileName}",
            FileName = fileName,
            FileSize = 1024,
            Entries = Array.Empty<OdinTarEntry>(),
            HasMd5Suffix = fileName.EndsWith(".md5"),
        };
        Assert.Equal(expected, info.PackageHint);
    }
}

public class ImeiValidationTests
{
    [Fact]
    public void ExtractImei_valid_parcel_returns_digits()
    {
        // Real parcel: IMEI 354237929314284 (15 digits). The ASCII column shows interleaved
        // dots from the UTF-16 encoding; the code extracts only digit chars.
        var parcel = @"Result: Parcel(
  0x00000000: 00000000 0000000f 00330035 00340032 '........3.5.4.2.'
  0x00000010: 00330037 00390032 00390033 00310034 '3.7.9.2.9.3.1.4.'
  0x00000020: 00320038 00000034                   '2.8.4...        ')";
        var imei = AdbService.ExtractImeiFromServiceCall(parcel);
        Assert.NotNull(imei);
        Assert.Equal(15, imei.Length);
        Assert.Equal("354237929314284", imei);
    }

    [Fact]
    public void ExtractImei_all_zeros_returns_null()
    {
        var parcel = @"Result: Parcel(
  0x00000000: 00000000 00000000 00000000 00000000 '........0.0.0.0.'
  0x00000010: 00000000 00000000 00000000 00000000 '0.0.0.0.0.0.0.0.'
  0x00000020: 00000000                            '0.......        ')";
        Assert.Null(AdbService.ExtractImeiFromServiceCall(parcel));
    }

    [Fact]
    public void ExtractImei_empty_returns_null()
        => Assert.Null(AdbService.ExtractImeiFromServiceCall(""));

    [Fact]
    public void ExtractImei_null_returns_null()
        => Assert.Null(AdbService.ExtractImeiFromServiceCall(null!));
}

public class DeviceProbeServiceTests
{
    [Theory]
    [InlineData("samsung/pa3qxxx/pa3q:16/BP2A.250605.031.A3/S938BXXS6BYIF_OXM6BYIF:user/release-keys",
        "S938BXXS6BYIF", "OXM6BYIF")]
    [InlineData("samsung/pa3qxxx/pa3q:16/BP2A/S938BXXS9BZCH:user/release-keys",
        "S938BXXS9BZCH", null)]
    public void ExtractSamsungPda_from_fingerprint(string fp, string expectedPda, string? expectedCsc)
    {
        var props = new Dictionary<string, string>();
        var (pda, csc) = DeviceProbeService.ExtractSamsungPda(props, fp);
        Assert.Equal(expectedPda, pda);
        Assert.Equal(expectedCsc, csc);
    }

    [Fact]
    public void ExtractSamsungPda_prefers_direct_prop()
    {
        var props = new Dictionary<string, string>
        {
            ["ro.build.PDA"] = "DIRECT_PDA",
        };
        var (pda, _) = DeviceProbeService.ExtractSamsungPda(props, "samsung/x/x/x/FP_PDA:user/keys");
        Assert.Equal("DIRECT_PDA", pda);
    }
}

public class DeviceInfoTests
{
    [Fact]
    public void IsSamsung_true_for_samsung_manufacturer()
    {
        var d = new DeviceInfo { Manufacturer = "Samsung" };
        Assert.True(d.IsSamsung);
    }

    [Fact]
    public void IsSamsung_false_for_google()
    {
        var d = new DeviceInfo { Manufacturer = "Google" };
        Assert.False(d.IsSamsung);
    }

    [Fact]
    public void PatchTargetPartition_is_init_boot_when_has_init_boot()
    {
        var d = new DeviceInfo { HasInitBoot = true };
        Assert.Equal("init_boot", d.PatchTargetPartition);
    }

    [Fact]
    public void PatchTargetPartition_is_boot_when_no_init_boot()
    {
        var d = new DeviceInfo { HasInitBoot = false };
        Assert.Equal("boot", d.PatchTargetPartition);
    }

    [Fact]
    public void DisplayName_uses_manufacturer_model()
    {
        var d = new DeviceInfo { Manufacturer = "Samsung", Model = "SM-S938B" };
        Assert.Equal("Samsung SM-S938B", d.DisplayName);
    }

    [Fact]
    public void DisplayName_falls_back_to_serial()
    {
        var d = new DeviceInfo { Serial = "ABC123" };
        Assert.Equal("ABC123", d.DisplayName);
    }
}

public class HashServiceTests
{
    [Fact]
    public async Task ComputeSha256_returns_lowercase_hex()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tmp, "hello world");
            var svc = new HashService();
            var hash = await svc.ComputeSha256Async(tmp);
            Assert.Matches("^[0-9a-f]{64}$", hash);
            Assert.Equal("b94d27b9934d3e08a52e52d7da7dabfac484efe37a5380ee9088f7ace2efcde9", hash);
        }
        finally
        {
            File.Delete(tmp);
        }
    }
}
