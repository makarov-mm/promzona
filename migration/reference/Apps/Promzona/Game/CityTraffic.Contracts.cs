namespace Promzona.Game;

public sealed partial class CityTraffic
{
    public int ChainStage {get;private set;}
    public int ChainsCompleted {get;private set;}
    private bool _chainContract;
    public double ContractMinimumHealth=>_chainContract?.5+ChainStage*.1:.5;
    public bool StartChainContract(){
        if(JobKind!=JobKind.None)return false;
        if(!StartContract(ChainStage==1?JobKind.Elimination:JobKind.VehicleDelivery))return false;
        _chainContract=true;_jobFare=300+ChainStage*200;_jobDeadline=Time+600-ChainStage*90;return true;
    }
    public int ContractActorId=>_jobKind is JobKind.VehicleDelivery or JobKind.PeaceBroadcast?_jobCarId:_jobKind==JobKind.Elimination?_jobPassengerId:-1;
    public double ContractVehicleHealth=>Cars.Find(c=>c.Id==_jobCarId)?.HealthRemaining??0;
    public bool ContractActive=>_jobKind is JobKind.VehicleDelivery or JobKind.Elimination;
    public bool StartContract(JobKind kind){
        if(PlayerUnavailable||_jobKind!=JobKind.None||kind is not (JobKind.VehicleDelivery or JobKind.Elimination))return false;
        if(kind==JobKind.VehicleDelivery){
            var car=Cars.Where(c=>!c.Hero&&!c.Destroyed&&!c.Burning&&!c.Falling&&c.Kind is 0 or 1 or 2&&c.OwnedGarageId==0&&c.HealthRemaining>=.75&&c.RoadLevel==0)
                .OrderBy(c=>Math.Abs((c.Position-PlayerPosition).Length-130)).FirstOrDefault();
            var garage=Garages.Where(g=>(g.Position-PlayerPosition).Length>80).OrderBy(g=>Math.Abs((g.Position-PlayerPosition).Length-260)).FirstOrDefault();
            if(car is null||garage==default)return Message("contract_unavailable");
            _jobCarId=car.Id;_jobPickup=car.Position;_jobTarget=car.Position;_contractDestination=garage.Door+new CityPoint(0,-3);_jobFare=250;_jobStage=0;
            // A parked, unoccupied handover vehicle; it retains its physical identity.
            car.Driverless=true;car.Body.Vx=car.Body.Vy=car.Body.W=car.Speed=0;FreePoliceReservation(car);
        }else{
            var person=Pedestrians.Where(p=>!p.IsPlayer&&!p.Dead&&!p.Detached&&p.ServiceId==0&&p.Boarding<0&&!p.Hailing&&p.RoadLevel==0)
                .OrderBy(p=>Math.Abs((p.Position-PlayerPosition).Length-150)+(p.Armed?0:100)).FirstOrDefault();
            if(person is null)return Message("contract_unavailable");
            _jobPassengerId=person.Id;_jobTarget=person.Position;_jobStage=1;_jobFare=350;person.Exiting=false;person.WaitingTransitRoute=0;person.TransitRetryAt=Time+600;
        }
        _jobKind=kind;_jobStarted=Time;_jobDeadline=Time+600;_jobLate=false;Message("contract_start");return true;
    }
    private CityPoint _contractDestination;
    public void CancelContract(){if(!ContractActive&&!PeaceJobActive)return;JobsFailed++;ClearJob();Message("job_lost");}
    private void ContractStep(double dt){
        if(Time>_jobDeadline){CancelContract();return;}
        if(_jobKind==JobKind.VehicleDelivery){
            var c=Cars.Find(c=>c.Id==_jobCarId);
            if(c is null||c.Destroyed||c.Falling){CancelContract();return;}
            if(PlayerControls(c))_jobStage=1;
            _jobTarget=_jobStage==0?c.Position:_contractDestination;
            bool valid=PlayerControls(c)&&c.HealthRemaining>=ContractMinimumHealth&&!c.Burning&&c.RoadLevel==0&&c.Grounded&&Math.Abs(c.Speed)<.8&&(c.Position-_contractDestination).Length<10;
            _jobStopped=valid?_jobStopped+dt:0;
            if(_jobStopped>1)CompleteContract();
        }else{
            var p=Pedestrians.Find(p=>p.Id==_jobPassengerId);
            if(p is null||p.Dead){CancelContract();return;}
            p.Exiting=false;_jobTarget=p.Position;
        }
    }
    private void ContractDeath(CityPedestrian person,bool playerCaused){
        if(_jobKind!=JobKind.Elimination||person.Id!=_jobPassengerId)return;
        if(playerCaused)CompleteContract();else CancelContract();
    }
    private void CompleteContract(){if(!ContractActive&&!PeaceJobActive)return;int pay=_jobFare;if(_chainContract){ChainStage++;if(ChainStage==3){ChainStage=0;ChainsCompleted++;pay+=500;}}Money=Math.Min(999999999,Money+pay);LastJobPay=pay;JobEarnings+=pay;JobsCompleted++;ClearJob();Sound("cash",PlayerPosition,.85,ui:true);Message("job_paid");}
}

