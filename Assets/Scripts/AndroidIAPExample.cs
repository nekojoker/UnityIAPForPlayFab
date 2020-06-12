using PlayFab;
using PlayFab.ClientModels;
using PlayFab.Json;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Purchasing;
using UnityEngine.Purchasing.Security;
using UnityEngine.UI;

public class AndroidIAPExample : MonoBehaviour, IStoreListener
{
    public Text text;
    public Text receiptText;
    public Text restoreText;

    ConfigurationBuilder builder;
    // Items list, configurable via inspector
    private List<CatalogItem> Catalog;

    // The Unity Purchasing system
    private static IStoreController storeController;
    private IExtensionProvider extensionProvider;

    // Bootstrap the whole thing
    public void Start()
    {
        // Make PlayFab log in
        Login();
    }

    public void OnGUI()
    {
        // This line just scales the UI up for high-res devices
        // Comment it out if you find the UI too large.
        GUI.matrix = Matrix4x4.TRS(new Vector3(0, 0, 0), Quaternion.identity, new Vector3(3, 3, 3));

        // if we are not initialized, only draw a message
        if (!IsInitialized)
        {
            GUILayout.Label("Initializing IAP and logging in...");
            return;
        }

        // Draw menu to purchase items
        foreach (var item in Catalog)
        {
            if (GUILayout.Button("Buy " + item.DisplayName))
            {
                // On button click buy a product
                BuyProductID(item.ItemId);
            }
        }
    }
    // This is invoked manually on Start to initiate login ops
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

    // This is invoked manually on Start to initialize UnityIAP
    public void InitializePurchasing()
    {
        // If IAP is already initialized, return gently
        if (IsInitialized) return;

        // Create a builder for IAP service
        builder = ConfigurationBuilder.Instance(StandardPurchasingModule.Instance());
        // Register each item from the catalog
        foreach (var item in Catalog)
        {
            builder.AddProduct(item.ItemId, ProductType.Consumable);
        }

        // Trigger IAP service initialization
        UnityPurchasing.Initialize(this, builder);
    }

    // We are initialized when StoreController and Extensions are set and we are logged in
    public bool IsInitialized
    {
        get
        {
            return storeController != null && Catalog != null;
        }
    }

    // This is automatically invoked automatically when IAP service is initialized
    public void OnInitialized(IStoreController controller, IExtensionProvider extensions)
    {
        storeController = controller;
        extensionProvider = extensions;

        //foreach (var product in controller.products.all)
        //{
        //    Debug.Log(product.metadata.localizedTitle);
        //    Debug.Log(product.metadata.localizedDescription);
        //    Debug.Log(product.metadata.localizedPriceString);
        //}
    }

    // This is automatically invoked automatically when IAP service failed to initialized
    public void OnInitializeFailed(InitializationFailureReason error)
    {
        Debug.Log("OnInitializeFailed InitializationFailureReason:" + error);
        text.text = "OnInitializeFailed InitializationFailureReason:" + error;
    }

    // This is automatically invoked automatically when purchase failed
    public void OnPurchaseFailed(Product product, PurchaseFailureReason failureReason)
    {
        Debug.Log(string.Format("OnPurchaseFailed: FAIL. Product: '{0}', PurchaseFailureReason: {1}", product.definition.storeSpecificId, failureReason));
        text.text = string.Format("OnPurchaseFailed: FAIL. Product: '{0}', PurchaseFailureReason: {1}", product.definition.storeSpecificId, failureReason);
    }

    // This is invoked automatically when successful purchase is ready to be processed
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
        //string receipt = builder.Configure<IAppleConfiguration>().appReceipt;
        //object receipt = PlayFabSimpleJson.DeserializeObject(e.purchasedProduct.receipt);

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

        // Deserialize receipt
        var googleReceipt = GooglePurchase.FromJson(e.purchasedProduct.receipt);

        // Invoke receipt validation
        // This will not only validate a receipt, but will also grant player corresponding items
        // only if receipt is valid.
        PlayFabClientAPI.ValidateGooglePlayPurchase(new ValidateGooglePlayPurchaseRequest()
        {
            // Pass in currency code in ISO format
            CurrencyCode = e.purchasedProduct.metadata.isoCurrencyCode,
            // Convert and set Purchase price
            PurchasePrice = (uint)(e.purchasedProduct.metadata.localizedPrice * 100),
            // Pass in the receipt
            ReceiptJson = googleReceipt.PayloadData.json,
            // Pass in the signature
            Signature = googleReceipt.PayloadData.signature
        }, result => {
            Debug.Log("Validation successful!");
               text.text = "Validation successful! ";
        },
           error => {
               Debug.Log("Validation failed: " + error.GenerateErrorReport());
               text.text = "Validation failed: " + error.GenerateErrorReport();

           }
        );
#endif
        return PurchaseProcessingResult.Complete;
    }

    // This is invoked manually to initiate purchase
    void BuyProductID(string productId)
    {
        // If IAP service has not been initialized, fail hard
        if (!IsInitialized) throw new Exception("IAP Service is not initialized!");

        // Pass in the product id to initiate purchase
        storeController.InitiatePurchase(productId);
    }
    public void RestorePurchases()
    {
        // If Purchasing has not yet been set up ...
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
            // ... begin restoring purchases
            Debug.Log("RestorePurchases started ...");

            // Fetch the Apple store-specific subsystem.
            var apple = extensionProvider.GetExtension<IAppleExtensions>();
            // Begin the asynchronous process of restoring purchases. Expect a confirmation response in 
            // the Action<bool> below, and ProcessPurchase if there are previously purchased products to restore.
            apple.RestoreTransactions((result) => {
                // The first phase of restoration. If no more responses are received on ProcessPurchase then 
                // no purchases are available to be restored.
                Debug.Log("RestorePurchases continuing: " + result + ". If no further messages, no purchases available to restore.");
                restoreText.text = "RestorePurchases continuing: " + result;
            });
        }
        // Otherwise ...
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
