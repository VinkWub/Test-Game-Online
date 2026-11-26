using UnityEngine;
using Unity.Netcode;
using TMPro;
using Unity.Collections;

public class ClientCarMove : NetworkBehaviour
{
    [SerializeField] private TextMeshProUGUI pname;
    [SerializeField] private NetworkVariable<FixedString128Bytes> network_pname = new NetworkVariable<FixedString128Bytes>(
    "Player: 0",
     NetworkVariableReadPermission.Everyone,
     NetworkVariableWritePermission.Server
    );
     
    float speed = 10f;
    float rot_speed = 100f;
    public override void OnNetworkSpawn()
    {
        string myname = "Player: " + (OwnerClientId+1);
        network_pname.Value = myname;
        pname.text = network_pname.Value.ToString();
    }
    private void Start()
    {
        if(!IsOwner) return;
        Camera.main.transform.position = this.transform.position - this.transform.forward * 5f + this.transform.up * 2f;
        Camera.main.transform.LookAt(this.transform.position);
        Camera.main.transform.SetParent(this.transform);
    }
    private void Update()
    {
        if(!IsOwner)
            return;
        
        float rot = Input.GetAxis("Horizontal") * rot_speed * Time.deltaTime;
        float tran = Input.GetAxis("Vertical") * speed * Time.deltaTime;

        UpdateInputServerRpc(rot, tran);
    }

    [Rpc(target: SendTo.Server)]
    private void UpdateInputServerRpc(float rot, float tran)
    {
        transform.Translate(0, 0, tran);
        transform.Rotate(0, rot, 0);
    }
}
