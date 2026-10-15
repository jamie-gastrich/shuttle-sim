namespace Messaging.Tests;
using Messaging;

public sealed class TopicSchemeTests
{
	[Fact]
	public void TopicScheme_builds_a_topic_branch_per_shuttle()
	{
		var topics = new TopicScheme("shuttle");

		Assert.Equal("shuttle/SH-01/command", topics.Command("SH-01"));
		Assert.Equal("shuttle/SH-01/ack", topics.Ack("SH-01"));
		Assert.Equal("shuttle/SH-01/status", topics.Status("SH-01"));
	}

	[Fact]
	public void TopicScheme_manager_filters_use_a_single_level_wildcard()
	{
		var topics = new TopicScheme("shuttle");

		Assert.Equal("shuttle/+/ack", topics.AllAcks);
		Assert.Equal("shuttle/+/status", topics.AllStatuses);
	}

	[Fact]
	public void TopicScheme_alarm_topics_use_the_per_shuttle_branch_and_the_wildcard()
	{
		var topics = new TopicScheme("shuttle");

		Assert.Equal("shuttle/SH-01/alarm", topics.Alarm("SH-01"));
		Assert.Equal("shuttle/+/alarm", topics.AllAlarms);
	}	
}
