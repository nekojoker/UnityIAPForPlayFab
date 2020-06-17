using PlayFab;
using PlayFab.ClientModels;
using PlayFab.Json;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Purchasing;
using UnityEngine.Purchasing.Security;
using UnityEngine.UI;
using System.Linq;

public class IAPExample : MonoBehaviour, IStoreListener
{
    public Text text;
    public Text receiptText;
    public Text restoreText;

    ConfigurationBuilder builder;
    private List<CatalogItem> Catalog;

    private static IStoreController storeController;
    private IExtensionProvider extensionProvider;

    public void Start()
    {
        Login();
    }

    public void OnGUI()
    {
        GUI.matrix = Matrix4x4.TRS(new Vector3(0, 0, 0), Quaternion.identity, new Vector3(3, 3, 3));

        if (!IsInitialized)
        {
            GUILayout.Label("Initializing IAP and logging in...");
            return;
        }

        foreach (var item in Catalog)
        {
            if (GUILayout.Button("Buy " + item.DisplayName))
            {
                BuyProductID(item.ItemId);
            }
        }
    }

    private void Login()
    {
#if UNITY_IOS
        PlayFabClientAPI.LoginWithIOSDeviceID(new LoginWithIOSDeviceIDRequest()
        {
            CreateAccount = true,
            DeviceId = SystemInfo.deviceUniqueIdentifier
        }, result =>
        {
            Debug.Log("Logged in");

            // Refresh available items
            RefreshIAPItems();
        }, error => Debug.LogError(error.GenerateErrorReport()));
#elif UNITY_ANDROID
        PlayFabClientAPI.LoginWithAndroidDeviceID(new LoginWithAndroidDeviceIDRequest()
        {
            CreateAccount = true,
            AndroidDeviceId = SystemInfo.deviceUniqueIdentifier
        }, result =>
        {
            Debug.Log("Logged in");
            text.text = "Logged in";

            // Refresh available items
            RefreshIAPItems();
        }, error => Debug.LogError(error.GenerateErrorReport()));
#endif
    }

    private void RefreshIAPItems()
    {
        PlayFabClientAPI.GetCatalogItems(new GetCatalogItemsRequest()
           , result =>
        {
            Catalog = result.Catalog;

            // Make UnityIAP initialize
            InitializePurchasing();
        }, error => Debug.LogError(error.GenerateErrorReport()));
    }

    public void InitializePurchasing()
    {
        if (IsInitialized) return;

        builder = ConfigurationBuilder.Instance(StandardPurchasingModule.Instance());

        // Register each item from the catalog
        foreach (var item in Catalog.FindAll(x => x.ItemClass == "Consumable"))
            builder.AddProduct(item.ItemId, ProductType.Consumable);

        foreach (var item in Catalog.FindAll(x => x.ItemClass == "NonConsumable"))
            builder.AddProduct(item.ItemId, ProductType.NonConsumable);

        foreach (var item in Catalog.FindAll(x => x.ItemClass == "Subscription"))
            builder.AddProduct(item.ItemId, ProductType.Subscription);

        // Trigger IAP service initialization
        UnityPurchasing.Initialize(this, builder);
    }

    public bool IsInitialized
    {
        get => storeController != null && extensionProvider != null && Catalog != null;
    }

    public void OnInitialized(IStoreController controller, IExtensionProvider extensions)
    {
        storeController = controller;
        extensionProvider = extensions;
    }

    public void OnInitializeFailed(InitializationFailureReason error)
    {
        Debug.Log("OnInitializeFailed InitializationFailureReason:" + error);
        text.text = "OnInitializeFailed InitializationFailureReason:" + error;
    }

    public void OnPurchaseFailed(Product product, PurchaseFailureReason failureReason)
    {
        Debug.Log(string.Format("OnPurchaseFailed: FAIL. Product: '{0}', PurchaseFailureReason: {1}", product.definition.storeSpecificId, failureReason));
        text.text = string.Format("OnPurchaseFailed: FAIL. Product: '{0}', PurchaseFailureReason: {1}", product.definition.storeSpecificId, failureReason);
    }

    public PurchaseProcessingResult ProcessPurchase(PurchaseEventArgs e)
    {
        // NOTE: this code does not account for purchases that were pending and are
        // delivered on application start.
        // Production code should account for such case:
        // More: https://docs.unity3d.com/ScriptReference/Purchasing.PurchaseProcessingResult.Pending.html

        if (!IsInitialized)
        {
            return PurchaseProcessingResult.Complete;
        }

        // Test edge case where product is unknown
        if (e.purchasedProduct == null)
        {
            Debug.LogWarning("Attempted to process purchase with unknown product. Ignoring");
            text.text = "Attempted to process purchase with unknown product. Ignoring";
            return PurchaseProcessingResult.Complete;
        }

        // Test edge case where purchase has no receipt
        if (string.IsNullOrEmpty(e.purchasedProduct.receipt))
        {
            Debug.LogWarning("Attempted to process purchase with no receipt: ignoring");
            text.text = "Attempted to process purchase with no receipt: ignoring";
            return PurchaseProcessingResult.Complete;
        }

        Debug.Log("Processing transaction: " + e.purchasedProduct.transactionID);
        receiptText.text = e.purchasedProduct.receipt;

#if UNITY_IOS
        var wrapper = (Dictionary<string, object>)MiniJson.JsonDecode(e.purchasedProduct.receipt);
     
        var store = (string)wrapper["Store"];
        var payload = (string)wrapper["Payload"]; // For Apple this will be the base64 encoded ASN.1 receipt

        PlayFabClientAPI.ValidateIOSReceipt(new ValidateIOSReceiptRequest
        {
            CurrencyCode = e.purchasedProduct.metadata.isoCurrencyCode,
            PurchasePrice = (int)e.purchasedProduct.metadata.localizedPrice * 100,
            ReceiptData = payload
        }, result => {
            Debug.Log("Validation successful!");
            text.text = "Validation successful! ";
        },
           error => {
               Debug.Log("Validation failed: " + error.GenerateErrorReport());
               text.text = "Validation failed: " + error.GenerateErrorReport();
           }
        );

#elif UNITY_ANDROID

        var googleReceipt = GooglePurchase.FromJson(e.purchasedProduct.receipt);

        PlayFabClientAPI.ValidateGooglePlayPurchase(new ValidateGooglePlayPurchaseRequest()
        {
            CurrencyCode = e.purchasedProduct.metadata.isoCurrencyCode,
            PurchasePrice = (uint)(e.purchasedProduct.metadata.localizedPrice * 100),
            ReceiptJson = googleReceipt.PayloadData.json,
            Signature = googleReceipt.PayloadData.signature
        }, result =>
        {
            Debug.Log("Validation successful!");
            text.text = "Validation successful! ";
        },
           error =>
           {
               Debug.Log("Validation failed: " + error.GenerateErrorReport());
               text.text = "Validation failed: " + error.GenerateErrorReport();
           }
        );
#endif
        return PurchaseProcessingResult.Complete;
    }

    void BuyProductID(string productId)
    {
        if (!IsInitialized)
            throw new Exception("IAP Service is not initialized!");

        storeController.InitiatePurchase(productId);
    }
    public void RestorePurchases()
    {
        if (!IsInitialized)
        {
            // ... report the situation and stop restoring. Consider either waiting longer, or retrying initialization.
            Debug.Log("RestorePurchases FAIL. Not initialized.");
            return;
        }

        // If we are running on an Apple device ... 
        if (Application.platform == RuntimePlatform.IPhonePlayer ||
            Application.platform == RuntimePlatform.OSXPlayer)
        {
            Debug.Log("RestorePurchases started ...");

            var apple = extensionProvider.GetExtension<IAppleExtensions>();
            // Begin the asynchronous process of restoring purchases. Expect a confirmation response in 
            // the Action<bool> below, and ProcessPurchase if there are previously purchased products to restore.
            apple.RestoreTransactions((result) =>
            {
                // The first phase of restoration. If no more responses are received on ProcessPurchase then 
                // no purchases are available to be restored.
                Debug.Log("RestorePurchases continuing: " + result + ". If no further messages, no purchases available to restore.");
                restoreText.text = "RestorePurchases continuing: " + result;
            });
        }
        else
        {
            // We are not running on an Apple device. No work is necessary to restore purchases.
            Debug.Log("RestorePurchases FAIL. Not supported on this platform. Current = " + Application.platform);
        }
    }
}

// The following classes are used to deserialize JSON results provided by IAP Service
// Please, note that JSON fields are case-sensitive and should remain fields to support Unity Deserialization via JsonUtilities
public class JsonData
{
    // JSON Fields, ! Case-sensitive
    public string orderId;
    public string packageName;
    public string productId;
    public long purchaseTime;
    public int purchaseState;
    public string purchaseToken;
}

public class PayloadData
{
    public JsonData JsonData;

    // JSON Fields, ! Case-sensitive
    public string signature;
    public string json;

    public static PayloadData FromJson(string json)
    {
        var payload = JsonUtility.FromJson<PayloadData>(json);
        payload.JsonData = JsonUtility.FromJson<JsonData>(payload.json);
        return payload;
    }
}

public class GooglePurchase
{
    public PayloadData PayloadData;

    // JSON Fields, ! Case-sensitive
    public string Store;
    public string TransactionID;
    public string Payload;

    public static GooglePurchase FromJson(string json)
    {
        var purchase = JsonUtility.FromJson<GooglePurchase>(json);
        purchase.PayloadData = PayloadData.FromJson(purchase.Payload);
        return purchase;
    }
}
