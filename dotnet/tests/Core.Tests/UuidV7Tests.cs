using Rcinel.Translate.Core;

namespace Rcinel.Translate.Core.Tests;

public class UuidV7Tests
{
  [Fact]
  public void New_ids_are_valid_lowercase_version_7()
  {
    string id = UuidV7.New();
    Assert.True(UuidV7.IsValid(id), id);
    Assert.Equal(id.ToLowerInvariant(), id);
    Assert.Equal('7', id[14]);
    Assert.True(Guid.TryParse(id, out _));
  }

  [Fact]
  public void Ids_made_in_a_row_sort_in_creation_order()
  {
    // Thousands within the same millisecond: the counter in rand_a must keep the order.
    List<string> ids = [.. Enumerable.Range(0, 10_000).Select(_ => UuidV7.New())];
    List<string> sorted = [.. ids.OrderBy(i => i, StringComparer.Ordinal)];
    Assert.Equal(sorted, ids);
    Assert.Equal(ids.Count, ids.Distinct().Count());
  }

  [Fact]
  public void The_timestamp_is_the_creation_time()
  {
    DateTimeOffset at = new(2026, 10, 31, 12, 30, 15, 123, TimeSpan.Zero);
    string id = UuidV7.New(at.AddYears(5));
    Assert.Equal(at.AddYears(5), UuidV7.GetTimestamp(id));
    Assert.Equal(new DateTimeOffset(2026, 10, 31, 0, 0, 0, TimeSpan.Zero), UuidV7.GetTimestamp("01a18f41-8c00-7000-8000-000000000000"));
  }

  [Theory]
  [InlineData("0192F3A0-1C4E-7A10-8000-000000000001")] // uppercase
  [InlineData("0192f3a0-1c4e-4a10-8000-000000000001")] // version 4
  [InlineData("0192f3a0-1c4e-7a10-c000-000000000001")] // wrong variant
  [InlineData("0192f3a01c4e7a108000000000000001")]     // no dashes
  [InlineData("save-button")]
  [InlineData("")]
  public void Rejects_what_is_not_a_canonical_uuidv7(string id) => Assert.False(UuidV7.IsValid(id));
}
