using AdhdTimeOrganizer.Core.domain.rule;
using FluentAssertions;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Services;

/// <summary>
/// FO8's shared rule, without a database: the median, and silence below the threshold. The to-do read that applies it
/// is <c>Endpoints.TodoListItemEstimateFitTests</c>.
/// </summary>
public class EstimateFitRuleTests
{
    [Fact(DisplayName = "FO8: an odd count takes the middle value, whatever the input order")]
    public void OddCount_TakesTheMiddle() =>
        EstimateFitRule.MedianOrSilent([10, 0.5, 2, 1, 1.5], 5).Should().Be(1.5);

    [Fact(DisplayName = "FO8: an even count takes the mean of the two middle values")]
    public void EvenCount_AveragesTheMiddlePair() =>
        EstimateFitRule.MedianOrSilent([4, 1, 2, 3], 3).Should().Be(2.5);

    [Fact(DisplayName = "FO8: below the threshold, and on no samples at all, the rule says nothing")]
    public void BelowTheThreshold_IsSilent()
    {
        EstimateFitRule.MedianOrSilent([1, 2, 3, 4], 5).Should().BeNull();
        EstimateFitRule.MedianOrSilent([], 0).Should().BeNull();
    }
}
