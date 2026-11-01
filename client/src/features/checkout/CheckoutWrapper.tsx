import { Elements } from "@stripe/react-stripe-js";
import CheckoutPage from "./CheckoutPage";
import { loadStripe } from "@stripe/stripe-js";
import { useAppDispatch } from "../../app/store/configureStore";
import { useCallback, useEffect, useState } from "react";
import { Alert, Button } from "@mui/material";
import { getErrorMessages } from "../../app/api/contracts";
import agent from "../../app/api/agent";
import { setBasket } from "../basket/basketSlice";
import LoadingComponent from "../../app/layout/LoadingComponent";

const stripePromise = loadStripe('pk_test_51N4fBPHPM29Ojk7KtOizBdXzC0o6ZZXGZs4pOQyzR7B69zGOz0QGPIp9XseVwyI54MeeaB7G4h9SG8gJHEz2TzK400r6PIq7jB')

export default function CheckoutWrapper() {
    const dispatch = useAppDispatch();
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState('');

    const preparePayment = useCallback(() => {
        setLoading(true);
        setError('');
        return agent.Payments.createPaymentIntent()
            .then(basket => dispatch(setBasket(basket)))
            .catch(error => setError(getErrorMessages(error).join('\n')))
            .finally(() => setLoading(false));
    }, [dispatch]);

    useEffect(() => { preparePayment(); }, [preparePayment]);
    
    if (loading) return <LoadingComponent message="Loading checkout..." />
    if (error) return <Alert severity="error" action={<Button onClick={preparePayment}>Retry</Button>}>{error}</Alert>;

    return (
        <Elements stripe={stripePromise}>
            <CheckoutPage />
        </Elements>
    )
}
