namespace EzMultiLib.Peers
{
	public class Peer
	{
		public int Id { get; }

		public Peer(int id)
		{
			Id = id;
		}

		public override string ToString() => $"{GetType().Name}({Id})";
	}
}
