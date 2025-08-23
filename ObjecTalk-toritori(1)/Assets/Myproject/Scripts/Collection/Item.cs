using UnityEngine;

[CreateAssetMenu(fileName = "Items", menuName = "Items/Item")]
public class Item : ScriptableObject
{
    [SerializeField]
    private string itemName;
    [SerializeField]
    private Sprite itemImage;
    public string MyItemName { get => itemName; private set => itemName = value; }
    public Sprite MyItemImage { get => itemImage; private set => itemImage = value; }
}