namespace FleetBackend.Services
{
    public interface ICommunicationGateway
    {
        void BroadcastRobotStateToUnity(Models.RobotState state);
        void SendCommandToRobot(string robotId, string command, object payload);
    }

    public class CommunicationGateway : ICommunicationGateway
    {
        public void BroadcastRobotStateToUnity(Models.RobotState state) { throw new System.NotImplementedException(); }
        public void SendCommandToRobot(string robotId, string command, object payload) { throw new System.NotImplementedException(); }
    }
}
