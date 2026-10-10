namespace Messaging.Tests;

public sealed class IdempotencyGuardTests
{
	[Fact]
	public void IdempotencyGuard_reports_a_seen_id_as_duplicate()
	{
		var guard = new IdempotencyGuard(capacity: 3);

		guard.MarkCompleted("command-1");

		Assert.True(guard.IsDuplicate("command-1"));
		Assert.False(guard.IsDuplicate("command-2"));
	}

	[Fact]
	public void IdempotencyGuard_forgets_the_oldest_id_at_capacity()
	{
		var guard = new IdempotencyGuard(capacity: 2);
		guard.MarkCompleted("command-1");
		guard.MarkCompleted("command-2");
		guard.MarkCompleted("command-3");

		Assert.False(guard.IsDuplicate("command-1"));
		Assert.True(guard.IsDuplicate("command-2"));
		Assert.True(guard.IsDuplicate("command-3"));
	}
}
