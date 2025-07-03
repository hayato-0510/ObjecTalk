using UnityEngine;

public class SlotGrid : MonoBehaviour
{
    [SerializeField]
    private GameObject slotPrefab;

    private int slotNumber = 30;

    [SerializeField]
    private Item[] allItems;　//ここは後ほどユーザーデータから持ってくるように実装する

    void Start()
    {
        for (int i = 0; i < slotNumber; i++)
        {
            GameObject slotObj = Instantiate(slotPrefab, this.transform);

            Slot slot = slotObj.GetComponent<Slot>();

            //スロットにアイテムをセットしたい
            if (i < allItems.Length)
            {
                slot.SetItem(allItems[i]);
            }
            else
            {
                slot.SetItem(null);
            }
                
        }
    }
}
