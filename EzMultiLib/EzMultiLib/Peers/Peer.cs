namespace EzMultiLib.Peers
{
	public sealed class Peer
	{
		public int Id { get; }

		internal Peer(int id)
		{
			Id = id;
		}

		public override int GetHashCode() => Id;
		public override bool Equals(object? obj)
		{
			return obj is Peer other && other.Id == Id;
		}
	}
}
