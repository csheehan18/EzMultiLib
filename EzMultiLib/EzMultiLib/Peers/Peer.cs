using System;

namespace EzMultiLib.Peers
{
	public class Peer
	{
		public int Id { get; }

		public Peer(int id)
		{
			Id = id;
		}

		// Dropping a connection is the transport's business, so the Peer a transport
		// hands out overrides this. The base throws rather than doing nothing: a kick
		// that quietly does not happen leaves a misbehaving client connected and says
		// nothing about it.
		public virtual void Kick() =>
			throw new NotSupportedException(
				$"{GetType().Name} does not implement Kick. Override it on the Peer your transport hands out.");

		public override string ToString() => $"{GetType().Name}({Id})";
	}
}
