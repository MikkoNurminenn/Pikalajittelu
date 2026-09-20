namespace MikkoMods.Storage
{
    public sealed class ReservationReply
    {
        public readonly long Owner;
        public bool Expired;
        public bool Received { get; private set; }
        public bool? Granted { get; private set; }
        public ReservationReply(long owner) { Owner = owner; }
        public bool Receive(long sender, bool granted)
        {
            if (sender != Owner || Received) return false;
            Received = true;
            if (!Expired) Granted = granted;
            return true;
        }
    }
}
