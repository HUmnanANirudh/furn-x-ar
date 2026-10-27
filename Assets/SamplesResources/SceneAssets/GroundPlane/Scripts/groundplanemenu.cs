using UnityEngine;

public class groundplanemenu : MonoBehaviour
{
    public GameObject orange_chair;
    public GameObject white_chair;
    public GameObject black_chair;
    public GameObject LOUIS;
    public GameObject Sofa_Shell;
    public GameObject sofa_vile;
    public GameObject sofa_decor1;
    public GameObject sofa_decoe2;
    public GameObject Circle_Chair;
    public GameObject woodsoftchair;
    public GameObject wood_chair;
    public GameObject Jack_Chair;
    public GameObject l_sofa;
    public GameObject sofa_taina;
    public GameObject apt_sofa;

    GameObject[] AllModels => new[]
    {
        orange_chair, white_chair, black_chair, LOUIS, Sofa_Shell,
        sofa_vile, sofa_decor1, sofa_decoe2, Circle_Chair, woodsoftchair,
        wood_chair, Jack_Chair, l_sofa, sofa_taina, apt_sofa
    };

    void SelectOnly(GameObject selected)
    {
        foreach (var model in AllModels)
        {
            if (model != null)
                model.SetActive(model == selected);
        }
    }

    public void openOrange_chair() => SelectOnly(orange_chair);
    public void openWhite_chair() => SelectOnly(white_chair);
    public void openBlack_chair() => SelectOnly(black_chair);
    public void openLouis() => SelectOnly(LOUIS);
    public void openSofa_Shell() => SelectOnly(Sofa_Shell);
    public void openSofa_vile() => SelectOnly(sofa_vile);
    public void openSofa_Decor1() => SelectOnly(sofa_decor1);
    public void openSofa_decor2() => SelectOnly(sofa_decoe2);
    public void openCircle_chair() => SelectOnly(Circle_Chair);
    public void openWoodsoftchair() => SelectOnly(woodsoftchair);
    public void openWood_chair() => SelectOnly(wood_chair);
    public void openJack_chair() => SelectOnly(Jack_Chair);
    public void openL_Sofa() => SelectOnly(l_sofa);
    public void openSofa_taina() => SelectOnly(sofa_taina);
    public void openApt_sofa() => SelectOnly(apt_sofa);
}
