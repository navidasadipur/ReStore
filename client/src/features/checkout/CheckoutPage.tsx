import { Alert, Box, Button, Paper, Step, StepLabel, Stepper, Typography } from "@mui/material";
import { useEffect, useState } from "react";
import { FieldValues, FormProvider, useForm } from "react-hook-form";
import AddressForm from "./AddressForm";
import PaymentForm from "./PaymentForm";
import Review from "./Review";
import { yupResolver } from '@hookform/resolvers/yup';
import { validationSchema } from "./checkoutValidation";
import agent from "../../app/api/agent";
import { useAppDispatch, useAppSelector } from "../../app/store/configureStore";
import { clearBasket } from "../basket/basketSlice";
import { LoadingButton } from "@mui/lab";
import { StripeElementType } from "@stripe/stripe-js";
import { getErrorMessages, getFormErrors } from "../../app/api/contracts";
import { useStripe, useElements, CardNumberElement } from "@stripe/react-stripe-js";

const steps = ['Shipping address', 'Review your order', 'Payment details'];

export default function CheckoutPage() {
    const [activeStep, setActiveStep] = useState(0);
    const [orderNumber, setOrderNumber] = useState(0);
    const [loading, setLoading] = useState(false);
    const dispatch = useAppDispatch();

    const [cardState, setCardState] = useState<{ elementError: { [key in StripeElementType]?: string } }>({ elementError: {} });
    const [cardComplete, setCardComplete] = useState<any>({ cardNumber: false, cardExpiry: false, cardCvc: false });

    const [paymentMessage, setPaymentMessage] = useState('');
    const [paymentSucceeded, setPaymentSucceeded] = useState(false);
    const [confirmedPaymentId, setConfirmedPaymentId] = useState<string | null>(null);

    const {basket} = useAppSelector(state => state.basket);

    const stripe = useStripe();
    const elements = useElements();
  
    function onCardInputChange(event: any) {
      setCardState(previous => ({
        ...previous,
        elementError: {
          ...previous.elementError,
          [event.elementType]: event.error?.message
        }
      }));
      setCardComplete((previous: any) => ({ ...previous, [event.elementType]: event.complete }));
    }

    function getStepContent(step: number) {
        switch (step) {
            case 0:
                return <AddressForm />;
            case 1:
                return <Review />;
            case 2:
                return <PaymentForm cardState={cardState} onCardInputChange={onCardInputChange} />;
            default:
                throw new Error('Unknown step');
        }
    }

    const currentValidationSchema = validationSchema[activeStep]

    const methods = useForm({
        mode: 'all',
        resolver: yupResolver(currentValidationSchema)
    });

    useEffect(() => {
        agent.Account.fetchAddress()
            .then(response => {
                if (response) {
                    methods.reset({ ...methods.getValues(), ...response, saveAddress: false })
                }
            })
            .catch(error => console.log(error));
    }, [methods]);

    async function submitOrder(data: FieldValues) {
        if (!confirmedPaymentId && (!stripe || !elements || !basket?.clientSecret)) {
            setPaymentMessage('Payment is not ready. Please try again.');
            return;
        }
        setLoading(true);
        setPaymentMessage('');
        const { nameOnCard, saveAddress, ...shippingAddress } = data;
        try {
            if (!confirmedPaymentId) {
                const cardElement = elements!.getElement(CardNumberElement);
                const paymentResult = await stripe!.confirmCardPayment(basket!.clientSecret!, {
                    payment_method: {
                        card: cardElement!,
                        billing_details: { name: nameOnCard }
                    }
                });
                if (paymentResult.paymentIntent?.status !== 'succeeded') {
                    setPaymentMessage(paymentResult.error?.message ?? 'The payment could not be confirmed.');
                    setPaymentSucceeded(false);
                    setActiveStep(steps.length);
                    return;
                }
                // If order creation fails after payment, a retry must not confirm/pay again.
                setConfirmedPaymentId(paymentResult.paymentIntent.id);
            }
            const orderNumber = await agent.Orders.create({ saveAddress, shippingAddress });
            setOrderNumber(orderNumber);
            setPaymentSucceeded(true);
            setPaymentMessage('Thank you - we have received your payment');
            setActiveStep(activeStep + 1);
            dispatch(clearBasket());
        } catch (error) {
            setPaymentMessage(getErrorMessages(error).join('\n'));
            const failure = getFormErrors(error, ['fullName', 'address1', 'address2', 'city', 'state', 'zip', 'country']);
            Object.entries(failure.fieldErrors).forEach(([field, message]) => methods.setError(field, { type: 'server', message }));
            if (Object.keys(failure.fieldErrors).length) setActiveStep(0);
        }
        finally { setLoading(false); }
    }

    const handleNext = async (data: FieldValues) => {
        if (activeStep == steps.length - 1) {
            await submitOrder(data);
        } else {
            setActiveStep(activeStep + 1);
        }
    };

    const handleBack = () => {
        setActiveStep(activeStep - 1);
    };

    function submitDisabled(): boolean {
        if(activeStep === steps.length - 1) {
            return !cardComplete.cardCvc 
                || !cardComplete.cardExpiry 
                || !cardComplete.cardNumber
                || !methods.formState.isValid
        } else {
            return !methods.formState.isValid
        }
    }

    return (
        <FormProvider {...methods}>
            <Paper variant="outlined" sx={{ my: { xs: 3, md: 6 }, p: { xs: 2, md: 3 } }}>
                <Typography component="h1" variant="h4" align="center">
                    Checkout
                </Typography>
                <Stepper activeStep={activeStep} sx={{ pt: 3, pb: 5 }}>
                    {steps.map((label) => (
                        <Step key={label}>
                            <StepLabel>{label}</StepLabel>
                        </Step>
                    ))}
                </Stepper>
                <>
                    {activeStep === steps.length ? (
                        <>
                            <Typography variant="h5" gutterBottom>
                                {paymentMessage}
                            </Typography>
                            {paymentSucceeded ? (
                            <Typography variant="subtitle1">
                            Your order number is #{orderNumber}. We have not emailed your order
                            confirmation, and will not send you an update when your order has
                            shipped as this is a fake store!
                        </Typography>
                            ) : (
                                <Button variant='contained' onClick={handleBack}>
                                    Go back and try again
                                </Button>
                        )}

                        </>
                    ) : (
                        <form onSubmit={methods.handleSubmit(handleNext)}>
                            {paymentMessage && <Alert severity="error" sx={{ mb: 2 }}>{paymentMessage}</Alert>}
                            {getStepContent(activeStep)}
                            <Box sx={{ display: 'flex', justifyContent: 'flex-end' }}>
                                {activeStep !== 0 && (
                                    <Button onClick={handleBack} sx={{ mt: 3, ml: 1 }}>
                                        Back
                                    </Button>
                                )}
                                <LoadingButton
                                    loading={loading}
                                    disabled={submitDisabled()}
                                    variant="contained"
                                    type='submit'
                                    sx={{ mt: 3, ml: 1 }}
                                >
                                    {activeStep === steps.length - 1 ? 'Place order' : 'Next'}
                                </LoadingButton>
                            </Box>
                        </form>
                    )
                    }
                </>
            </Paper >
        </FormProvider>
    );
}
